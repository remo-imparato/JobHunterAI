using JobHunterAI.Application.AI;
using JobHunterAI.Domain;

namespace JobHunterAI.Automation;

public enum ApplicationReviewStatus
{
    AwaitingReview,
    Submitting,
    SubmissionAttempted,
    SubmissionUncertain,
    Faulted,
    Disposed
}

public sealed record ApplicationAnswerReview(string Selector, string Question, string Answer, bool NeedsUserInput);

public sealed record ApplicationReview(
    Guid SessionId, Guid CandidateId, Guid JobId, int Revision, string PageUrl,
    IReadOnlyList<DetectedField> Fields, IReadOnlyList<ApplicationAnswerReview> Answers,
    IReadOnlyList<string> Screenshots);

public sealed record ApplicationApproval(Guid SessionId, int Revision, Guid UserId, bool Approved);

public sealed record ApplicationSubmissionAttempt(Guid SessionId, Guid ApprovedByUserId, DateTimeOffset AttemptedAt);

public sealed class ApplicationAgent(
    ApplicationWorkflow workflow,
    IApplicationQuestionService questions,
    FormDetectionService detection,
    IScreenshotStore screenshots,
    IAutomationAuditLog audit)
{
    public async Task<ApplicationReviewSession> PrepareAsync(
        JobPosting job, CandidateProfile candidate, ApplicationDocuments documents,
        CancellationToken cancellationToken = default)
    {
        List<ApplicationAnswerReview> answers = [];
        ApplicationSession session = await workflow.PrepareForReviewAsync(job, candidate, documents,
            async (page, fields, token) =>
            {
                List<ApplicationQuestion> prompts = [];
                foreach (DetectedField field in fields)
                {
                    prompts.Add(QuestionDetectionService.ToQuestion(field));
                }

                IReadOnlyList<SuggestedAnswer> suggestions = prompts.Count == 0 ? [] :
                    await questions.SuggestAnswersAsync(candidate, job, prompts.AsReadOnly(), token);
                if (suggestions.Count != fields.Count)
                {
                    throw new InvalidOperationException("The answer service must return one suggestion per question.");
                }

                for (int i = 0; i < fields.Count; i++)
                {
                    DetectedField field = fields[i];
                    SuggestedAnswer suggestion = suggestions[i];
                    bool valid = suggestion.Question == prompts[i].Question && !suggestion.NeedsUserInput &&
                        ApplicationReviewSession.IsValidAnswer(field, suggestion.Answer);
                    string answer = valid ? suggestion.Answer : string.Empty;
                    if (valid)
                    {
                        await ApplicationReviewSession.FillAnswerAsync(page, field, answer, token);
                    }

                    answers.Add(new ApplicationAnswerReview(field.Selector, prompts[i].Question, answer, !valid));
                }

                audit.Record("Suggest", $"Prepared {answers.Count} answer suggestions for user review.");
            }, cancellationToken);
        return new ApplicationReviewSession(session, candidate.Id, job.Id, answers, detection, screenshots, audit);
    }
}

/// <summary>Owns the live page. Approval applies only to the current review revision; submission attempts are never retried.</summary>
public sealed class ApplicationReviewSession : IAsyncDisposable
{
    private readonly ApplicationSession _session;
    private readonly FormDetectionService _detection;
    private readonly IScreenshotStore _screenshots;
    private readonly IAutomationAuditLog _audit;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ApplicationAnswerReview> _answers;
    private readonly List<string> _shots;
    private readonly Guid _candidateId;
    private readonly Guid _jobId;
    private readonly string _pageUrl;
    private int _revision = 1;

    internal ApplicationReviewSession(ApplicationSession session, Guid candidateId, Guid jobId,
        List<ApplicationAnswerReview> answers, FormDetectionService detection, IScreenshotStore screenshots, IAutomationAuditLog audit)
    {
        _session = session;
        _candidateId = candidateId;
        _jobId = jobId;
        _answers = answers;
        _shots = new List<string>(session.Screenshots);
        _pageUrl = session.Page.CurrentUrl;
        _detection = detection;
        _screenshots = screenshots;
        _audit = audit;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public ApplicationReviewStatus Status { get; private set; } = ApplicationReviewStatus.AwaitingReview;

    public async Task<ApplicationReview> GetReviewAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RequireReview();
            List<DetectedField> fields = [];
            foreach (DetectedField field in _session.Fields)
            {
                fields.Add(field with { Options = new List<string>(field.Options).AsReadOnly() });
            }

            return new ApplicationReview(Id, _candidateId, _jobId, _revision, _pageUrl, fields.AsReadOnly(),
                new List<ApplicationAnswerReview>(_answers).AsReadOnly(), new List<string>(_shots).AsReadOnly());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAnswerAsync(string selector, string answer, int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RequireReview();
            RequireRevision(expectedRevision);
            int index = _answers.FindIndex(a => a.Selector == selector);
            if (index < 0)
            {
                throw new ArgumentException("The selector must identify a detected question.", nameof(selector));
            }

            DetectedField field = _session.QuestionFields[index];
            if (!IsValidAnswer(field, answer))
            {
                throw new ArgumentException("Answer must be nonempty, fit the field length and match a select option where applicable.", nameof(answer));
            }

            cancellationToken.ThrowIfCancellationRequested();
            _revision++;
            try
            {
                await FillAnswerAsync(_session.Page, field, answer, cancellationToken);
                _answers[index] = _answers[index] with { Answer = answer, NeedsUserInput = false };
                _shots.Add(await _screenshots.SaveAsync("review", await _session.Page.ScreenshotAsync(cancellationToken), cancellationToken));
                _audit.Record("Review", $"Updated answer in session {Id}, revision {_revision}. Approval is required.");
            }
            catch
            {
                Status = ApplicationReviewStatus.Faulted;
                _audit.Record("Failed", $"Answer update failed for session {Id}; prepare a new review.");
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ApplicationSubmissionAttempt> ApproveAndSubmitAsync(ApplicationApproval approval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RequireReview();
            if (!approval.Approved || approval.SessionId != Id || approval.UserId == Guid.Empty)
            {
                throw new InvalidOperationException("Explicit user approval for this session is required.");
            }

            RequireRevision(approval.Revision);
            foreach (ApplicationAnswerReview answer in _answers)
            {
                if (answer.NeedsUserInput)
                {
                    throw new InvalidOperationException("Resolve questions requiring user input before approval.");
                }
            }

            IReadOnlyList<DetectedField> current = _detection.Detect(await _session.Page.ReadFormFieldsAsync(cancellationToken));
            if (_session.Page.CurrentUrl != _pageUrl || !SameFields(_session.Fields, current))
            {
                Status = ApplicationReviewStatus.Faulted;
                _audit.Record("ReviewInvalidated", $"Page changed in session {Id}; prepare a new review.");
                throw new InvalidOperationException("The application page changed; prepare and review it again.");
            }

            _shots.Add(await _screenshots.SaveAsync("approved", await _session.Page.ScreenshotAsync(cancellationToken), cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            _audit.Record("Approve", $"User {approval.UserId} approved session {Id}, revision {_revision}.");
            DateTimeOffset attemptedAt = DateTimeOffset.UtcNow;
            Status = ApplicationReviewStatus.Submitting;
            try
            {
                await _session.Page.ClickSubmitAsync(cancellationToken);
                Status = ApplicationReviewStatus.SubmissionAttempted;
                _audit.Record("Submit", $"Submit control activated for session {Id}; employer acceptance is not verified.");
                return new ApplicationSubmissionAttempt(Id, approval.UserId, attemptedAt);
            }
            catch
            {
                Status = ApplicationReviewStatus.SubmissionUncertain;
                _audit.Record("SubmitUncertain", $"Submission outcome unknown for session {Id}. Do not retry automatically.");
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (Status != ApplicationReviewStatus.Disposed)
            {
                Status = ApplicationReviewStatus.Disposed;
                await _session.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static bool IsValidAnswer(DetectedField field, string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer) || (field.MaxLength is int max && answer.Length > max))
        {
            return false;
        }

        if (field.Kind != FieldKind.Select)
        {
            return true;
        }

        foreach (string option in field.Options)
        {
            if (option == answer)
            {
                return true;
            }
        }

        return false;
    }

    internal static Task FillAnswerAsync(IApplicationPage page, DetectedField field, string answer, CancellationToken token) =>
        field.Kind == FieldKind.Select ? page.SelectOptionAsync(field.Selector, answer, token) :
            page.FillTextAsync(field.Selector, answer, token);

    private void RequireReview()
    {
        if (Status != ApplicationReviewStatus.AwaitingReview)
        {
            throw new InvalidOperationException("This session is no longer awaiting review.");
        }
    }

    private void RequireRevision(int revision)
    {
        if (revision != _revision)
        {
            throw new InvalidOperationException("The review revision is stale. Review the current answers before approving.");
        }
    }

    private static bool SameFields(IReadOnlyList<DetectedField> expected, IReadOnlyList<DetectedField> actual)
    {
        if (expected.Count != actual.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Count; i++)
        {
            DetectedField left = expected[i];
            DetectedField right = actual[i];
            if (left.Selector != right.Selector || left.Kind != right.Kind || left.Name != right.Name ||
                left.Label != right.Label || left.Required != right.Required || left.MaxLength != right.MaxLength ||
                left.Options.Count != right.Options.Count)
            {
                return false;
            }

            for (int j = 0; j < left.Options.Count; j++)
            {
                if (left.Options[j] != right.Options[j])
                {
                    return false;
                }
            }
        }

        return true;
    }
}
