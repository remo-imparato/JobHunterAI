using JobHunterAI.Application.AI;
using JobHunterAI.Domain;

namespace JobHunterAI.AI;

internal static class AiValidation
{
    public static List<string> Clean(IReadOnlyList<string>? values, int maxItems = 20)
    {
        List<string> result = [];
        if (values is null)
        {
            return result;
        }

        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && result.Count < maxItems)
            {
                result.Add(value.Trim());
            }
        }

        return result;
    }

    public static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new AiResponseException($"The AI reply is missing {name}.");
}

public sealed class JobMatchingService(StructuredAiClient client) : IJobMatchingService
{
    private sealed record Reply(int? MatchScore, List<string>? Strengths, List<string>? Weaknesses,
        List<string>? RecommendedKeywords, List<string>? RiskFactors);

    public async Task<JobMatchResult> MatchAsync(
        CandidateProfile candidate, JobPosting job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(job);
        string system =
            "You evaluate how well a candidate fits a job. " + AiPromptData.UntrustedDataRule + " " + AiPromptData.TruthRule +
            " JSON shape: {\"matchScore\":0-100 integer,\"strengths\":[string],\"weaknesses\":[string]," +
            "\"recommendedKeywords\":[string],\"riskFactors\":[string]}.";
        string user = AiJson.Serialize(new { candidate = AiPromptData.Candidate(candidate), job = AiPromptData.Job(job) });
        Reply result = await client.RequestAsync<Reply>("JobMatching", system, user, reply =>
        {
            if (reply.MatchScore is null or < 0 or > 100)
            {
                throw new AiResponseException("The AI reply has an invalid matchScore.");
            }

            return reply;
        }, cancellationToken);
        return new JobMatchResult(result.MatchScore!.Value, AiValidation.Clean(result.Strengths),
            AiValidation.Clean(result.Weaknesses), AiValidation.Clean(result.RecommendedKeywords),
            AiValidation.Clean(result.RiskFactors));
    }
}

public sealed class ResumeTailoringService(StructuredAiClient client) : IResumeTailoringService
{
    private sealed record Reply(string? ContentMarkdown, List<string>? KeywordsUsed);

    public async Task<TailoredResumeResult> TailorAsync(
        CandidateProfile candidate, JobPosting job, string baseResumeMarkdown, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseResumeMarkdown);
        string system =
            "You tailor a resume to a job for ATS keyword fit. " + AiPromptData.UntrustedDataRule + " " + AiPromptData.TruthRule +
            " Only reorder, reword and emphasise content from the base resume. JSON shape: " +
            "{\"contentMarkdown\":string,\"keywordsUsed\":[string]}.";
        string user = AiJson.Serialize(new { candidate = AiPromptData.Candidate(candidate), job = AiPromptData.Job(job), baseResumeMarkdown });
        Reply reply = await client.RequestAsync<Reply>("ResumeTailoring", system, user, r =>
        {
            AiValidation.Required(r.ContentMarkdown, "contentMarkdown");
            return r;
        }, cancellationToken);
        return new TailoredResumeResult(reply.ContentMarkdown!.Trim(), AiValidation.Clean(reply.KeywordsUsed, 50));
    }
}

public sealed class CoverLetterService(StructuredAiClient client) : ICoverLetterService
{
    private sealed record Reply(string? ContentMarkdown);

    public async Task<CoverLetterResult> WriteAsync(
        CandidateProfile candidate, JobPosting job, Company company, CoverLetterTone tone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(company);
        string system =
            "You write a concise cover letter. " + AiPromptData.UntrustedDataRule + " " + AiPromptData.TruthRule +
            " JSON shape: {\"contentMarkdown\":string}.";
        string user = AiJson.Serialize(new
        {
            tone = tone.ToString(),
            company = new { company.Name },
            candidate = AiPromptData.Candidate(candidate),
            job = AiPromptData.Job(job)
        });
        Reply reply = await client.RequestAsync<Reply>("CoverLetter", system, user, r =>
        {
            AiValidation.Required(r.ContentMarkdown, "contentMarkdown");
            return r;
        }, cancellationToken);
        return new CoverLetterResult(reply.ContentMarkdown!.Trim());
    }
}

public sealed class ApplicationQuestionService(StructuredAiClient client) : IApplicationQuestionService
{
    private sealed record AnswerReply(string? Question, string? Answer, bool? NeedsUserInput);

    private sealed record Reply(List<AnswerReply>? Answers);

    public async Task<IReadOnlyList<SuggestedAnswer>> SuggestAnswersAsync(
        CandidateProfile candidate, JobPosting job, IReadOnlyList<ApplicationQuestion> questions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
        {
            return [];
        }

        string system =
            "You draft answers to job application questions for review by the candidate. " + AiPromptData.UntrustedDataRule + " " +
            AiPromptData.TruthRule + " If the candidate data does not support an answer, set needsUserInput to true and leave answer empty. " +
            "JSON shape: {\"answers\":[{\"question\":string,\"answer\":string,\"needsUserInput\":boolean}]} with one entry per question, same order.";
        string user = AiJson.Serialize(new { candidate = AiPromptData.Candidate(candidate), job = AiPromptData.Job(job), questions });
        Reply reply = await client.RequestAsync<Reply>("ApplicationQuestions", system, user, r =>
            r.Answers is not null && r.Answers.Count == questions.Count
                ? r : throw new AiResponseException("The AI reply did not answer every question."), cancellationToken);

        List<SuggestedAnswer> result = [];
        for (int i = 0; i < questions.Count; i++)
        {
            AnswerReply answer = reply.Answers![i];
            string text = (answer.Answer ?? string.Empty).Trim();
            if (questions[i].MaxLength is int max && text.Length > max)
            {
                text = text[..max];
            }

            result.Add(new SuggestedAnswer(questions[i].Question, text, answer.NeedsUserInput ?? text.Length == 0));
        }

        return result;
    }
}
