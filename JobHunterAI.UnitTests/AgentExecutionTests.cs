using JobHunterAI.Domain;

namespace JobHunterAI.UnitTests;

public sealed class AgentExecutionTests
{
    private static readonly DateTimeOffset StartedAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Complete_OnlyFinishesRunningExecutionAfterStart()
    {
        AgentExecution execution = new(Guid.NewGuid(), Guid.NewGuid(), "JobMatcher", StartedAt);

        Assert.Throws<InvalidOperationException>(() => execution.Complete(StartedAt.AddMinutes(-1)));
        execution.Complete(StartedAt.AddMinutes(1));

        Assert.Equal(AgentExecutionStatus.Succeeded, execution.Status);
        Assert.Throws<InvalidOperationException>(() => execution.Fail("RETRY", StartedAt.AddMinutes(2)));
    }

    [Fact]
    public void Fail_RecordsFailureCodeWithoutExceptionDetails()
    {
        AgentExecution execution = new(Guid.NewGuid(), Guid.NewGuid(), "ResumeTailor", StartedAt);

        execution.Fail("AI_UNAVAILABLE", StartedAt.AddMinutes(1));

        Assert.Equal(AgentExecutionStatus.Failed, execution.Status);
        Assert.Equal("AI_UNAVAILABLE", execution.FailureCode);
    }
}
