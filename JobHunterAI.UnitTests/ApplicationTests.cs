using JobHunterAI.Domain;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.UnitTests;

public sealed class ApplicationTests
{
    private static readonly DateTimeOffset CreatedAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Submit_RequiresUserApprovalAndRecordsAuditTrail()
    {
        DomainApplication application = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CreatedAt);
        Guid userId = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => application.Submit(Guid.NewGuid(), CreatedAt.AddMinutes(1)));

        application.RequestReview(Guid.NewGuid(), CreatedAt.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => application.Submit(Guid.NewGuid(), CreatedAt.AddMinutes(2)));
        application.Approve(Guid.NewGuid(), userId, CreatedAt.AddMinutes(2));
        application.Submit(Guid.NewGuid(), CreatedAt.AddMinutes(3));

        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(userId, application.ApprovedByUserId);
        Assert.Equal(3, application.Events.Count);
        Assert.Contains(application.Events, e => e.ToStatus == ApplicationStatus.Approved && e.ActorId == userId);
        Assert.Contains(application.Events, e => e.FromStatus == ApplicationStatus.Approved && e.ToStatus == ApplicationStatus.Submitted);
    }

    [Fact]
    public void ReReview_InvalidatesExistingApproval()
    {
        DomainApplication application = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CreatedAt);
        application.RequestReview(Guid.NewGuid(), CreatedAt.AddMinutes(1));
        application.Approve(Guid.NewGuid(), Guid.NewGuid(), CreatedAt.AddMinutes(2));

        application.RequestReview(Guid.NewGuid(), CreatedAt.AddMinutes(3));

        Assert.Null(application.ApprovedByUserId);
        Assert.Null(application.ApprovedAt);
        Assert.Throws<InvalidOperationException>(() => application.Submit(Guid.NewGuid(), CreatedAt.AddMinutes(4)));
    }

    [Fact]
    public void InvalidEvent_DoesNotMutateApplication()
    {
        DomainApplication application = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CreatedAt);
        Guid reviewEventId = Guid.NewGuid();
        application.RequestReview(reviewEventId, CreatedAt.AddMinutes(1));

        Assert.Throws<ArgumentException>(() => application.Approve(Guid.NewGuid(), Guid.NewGuid(), CreatedAt.AddMinutes(-1)));
        Assert.Throws<InvalidOperationException>(() => application.Approve(reviewEventId, Guid.NewGuid(), CreatedAt.AddMinutes(2)));

        Assert.Equal(ApplicationStatus.ReadyForReview, application.Status);
        Assert.Single(application.Events);
    }

    [Fact]
    public void SubmittedApplication_CanRecordAnOffer()
    {
        DomainApplication application = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CreatedAt);
        application.RequestReview(Guid.NewGuid(), CreatedAt);
        application.Approve(Guid.NewGuid(), Guid.NewGuid(), CreatedAt);
        application.Submit(Guid.NewGuid(), CreatedAt);

        application.RecordOffer(Guid.NewGuid(), CreatedAt.AddDays(1));

        Assert.Equal(ApplicationStatus.Offer, application.Status);
        Assert.Throws<InvalidOperationException>(() => application.Withdraw(Guid.NewGuid(), CreatedAt.AddDays(2)));
    }
}
