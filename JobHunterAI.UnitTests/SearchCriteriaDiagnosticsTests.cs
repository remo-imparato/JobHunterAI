using JobHunterAI.Domain;

namespace JobHunterAI.UnitTests;

public sealed class SearchCriteriaDiagnosticsTests
{
    [Fact]
    public void Diagnostics_ReportAllFailedFiltersWithoutRelaxingMatching()
    {
        CandidateSearchCriteria criteria = new(Guid.NewGuid(), ["Engineer"], ["Boston"],
            WorkArrangement.Remote, false, ["dotnet"], ["senior"]);

        JobFilterMismatch reasons = criteria.MismatchReasons("Sales", "Berlin", "Germany", WorkArrangement.OnSite, "senior java");

        Assert.Equal(JobFilterMismatch.Title | JobFilterMismatch.Location | JobFilterMismatch.WorkArrangement |
            JobFilterMismatch.RequiredKeywords | JobFilterMismatch.ExcludedKeywords, reasons);
        Assert.False(criteria.Matches("Sales", "Berlin", "Germany", WorkArrangement.OnSite, "senior java"));
    }

    [Fact]
    public void Diagnostics_PreserveCaseInsensitiveSubstringAndAllRequiredKeywordSemantics()
    {
        CandidateSearchCriteria criteria = new(Guid.NewGuid(), ["Engineer"], ["Boston"],
            WorkArrangement.Remote, false, ["dotnet", "SQL"], ["senior"]);

        Assert.Equal(JobFilterMismatch.None, criteria.MismatchReasons("Staff ENGINEER", "BOSTON", "USA", WorkArrangement.Remote, "Develop DOTNET and sql services"));
        Assert.True(criteria.Matches("Staff ENGINEER", "BOSTON", "USA", WorkArrangement.Remote, "Develop DOTNET and sql services"));
        Assert.Equal(JobFilterMismatch.RequiredKeywords, criteria.MismatchReasons("Engineer", "Boston", "USA", WorkArrangement.Remote, "Develop dotnet services"));
    }

    [Fact]
    public void Diagnostics_DoNotAssumeRemoteJobsSatisfyLocationOrUnknownArrangementFilters()
    {
        CandidateSearchCriteria criteria = new(Guid.NewGuid(), ["Engineer"], ["Boston"],
            WorkArrangement.Remote, false, [], []);
        Assert.Equal(JobFilterMismatch.Location, criteria.MismatchReasons("Engineer", "Worldwide", null, WorkArrangement.Remote, "Build"));
        Assert.Equal(JobFilterMismatch.WorkArrangement, criteria.MismatchReasons("Engineer", "Boston", null, null, "Build"));
    }
}
