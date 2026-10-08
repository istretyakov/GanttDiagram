using GanttDiagram.Domain.Entities;

namespace GanttDiagram.Tests.Unit;

/// <summary>Правила «снимок Jira и план» при импорте и пересинхронизации.</summary>
public class IssueSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly D5 = new(2026, 10, 5);
    private static readonly DateOnly D9 = new(2026, 10, 9);

    private static Issue NewIssue() => new() { Key = "PRJ-1", Summary = "Задача" };

    [Fact]
    public void NewIssue_PlanIsFilledFromJira()
    {
        var user = Guid.NewGuid();
        var issue = NewIssue();

        issue.ApplyJiraSnapshot(D5, D9, user, Now, Now);

        Assert.Equal(D5, issue.PlanStartDate);
        Assert.Equal(D9, issue.PlanEndDate);
        Assert.Equal(user, issue.PlanAssigneeId);
        Assert.False(issue.DiffersFromJira);
    }

    [Fact]
    public void NotModifiedIssue_PlanFollowsJira()
    {
        var issue = NewIssue();
        issue.ApplyJiraSnapshot(D5, D9, null, Now, Now);

        issue.ApplyJiraSnapshot(D5.AddDays(7), D9.AddDays(7), null, Now, Now);

        Assert.Equal(D5.AddDays(7), issue.PlanStartDate);
        Assert.False(issue.IsLocallyModified);
    }

    [Fact]
    public void LocallyModifiedIssue_ResyncKeepsPlan_UpdatesSnapshot()
    {
        var issue = NewIssue();
        issue.ApplyJiraSnapshot(D5, D9, null, Now, Now);
        issue.ApplyPlan(D5.AddDays(1), D9.AddDays(1), null, Now);

        issue.ApplyJiraSnapshot(D5.AddDays(14), D9.AddDays(14), null, Now, Now);

        Assert.True(issue.IsLocallyModified);
        Assert.Equal(D5.AddDays(1), issue.PlanStartDate);
        Assert.Equal(D5.AddDays(14), issue.JiraStartDate);
        Assert.True(issue.DiffersFromJira);
    }

    [Fact]
    public void ApplyPlan_SameAsJira_IsNotLocalModification()
    {
        var issue = NewIssue();
        issue.ApplyJiraSnapshot(D5, D9, null, Now, Now);

        issue.ApplyPlan(D5, D9, null, Now);

        Assert.False(issue.IsLocallyModified);
    }

    [Fact]
    public void ResetToJira_RestoresSnapshot()
    {
        var user = Guid.NewGuid();
        var issue = NewIssue();
        issue.ApplyJiraSnapshot(D5, D9, user, Now, Now);
        issue.ApplyPlan(D9, D9, null, Now);

        issue.ResetToJira();

        Assert.Equal(D5, issue.PlanStartDate);
        Assert.Equal(user, issue.PlanAssigneeId);
        Assert.False(issue.IsLocallyModified);
        Assert.False(issue.DiffersFromJira);
    }
}
