using System.Text.Json;
using GanttDiagram.Application.Jira;
using GanttDiagram.Application.Services;
using GanttDiagram.Domain.Entities;
using GanttDiagram.Infrastructure.Jira;

namespace GanttDiagram.Tests.Unit;

public class IssueHierarchyTests
{
    private static readonly HierarchyRule IncludesRule = new("Includes", LinkDirection.Outward);
    private static readonly HierarchyRule TestedByRule = new("Tests", LinkDirection.Inward);
    private static readonly HierarchyRule[] Rules = [IncludesRule, TestedByRule];

    private static Issue Issue(string key, params IssueLink[] links)
    {
        var issue = new Issue { Id = Guid.NewGuid(), Key = key, Summary = key };
        issue.Links.AddRange(links);
        return issue;
    }

    private static IssueLink Link(string type, LinkDirection direction, string key) => new()
    {
        LinkTypeName = type,
        Direction = direction,
        LinkedIssueKey = key,
        OutwardVerb = type == "Tests" ? "tests" : type == "Includes" ? "includes" : "blocks",
        InwardVerb = type == "Tests" ? "is tested by" : type == "Includes" ? "is included in" : "is blocked by",
    };

    [Fact]
    public void Includes_And_TestedBy_BuildTree()
    {
        // PRJ-1 includes PRJ-2; PRJ-1 is tested by TEST-5. Связь видна с обеих сторон.
        var story = Issue("PRJ-1", Link("Includes", LinkDirection.Outward, "PRJ-2"), Link("Tests", LinkDirection.Inward, "TEST-5"));
        var sub = Issue("PRJ-2", Link("Includes", LinkDirection.Inward, "PRJ-1"));
        var test = Issue("TEST-5", Link("Tests", LinkDirection.Outward, "PRJ-1"));

        var parents = IssueHierarchy.Compute([story, sub, test], Rules);

        Assert.False(parents.ContainsKey(story.Id));
        Assert.Equal(new IssueParent(story.Id, "includes"), parents[sub.Id]);
        Assert.Equal(new IssueParent(story.Id, "is tested by"), parents[test.Id]);
    }

    [Fact]
    public void LinkOnlyOnOneSide_StillWorks()
    {
        var story = Issue("PRJ-1");
        var test = Issue("TEST-5", Link("Tests", LinkDirection.Outward, "PRJ-1"));

        var parents = IssueHierarchy.Compute([story, test], Rules);

        Assert.Equal(story.Id, parents[test.Id].ParentId);
    }

    [Fact]
    public void OtherLinkTypes_AndParentsOutsideProject_AreIgnored()
    {
        var a = Issue("PRJ-1", Link("Blocks", LinkDirection.Outward, "PRJ-2"), Link("Includes", LinkDirection.Inward, "OTHER-9"));
        var b = Issue("PRJ-2");

        Assert.Empty(IssueHierarchy.Compute([a, b], Rules));
        Assert.Empty(IssueHierarchy.Compute([a, b], []));
    }

    [Fact]
    public void SeveralParents_FirstRuleThenSmallerKeyWins()
    {
        var child = Issue("PRJ-5",
            Link("Tests", LinkDirection.Outward, "PRJ-2"),
            Link("Includes", LinkDirection.Inward, "PRJ-10"),
            Link("Includes", LinkDirection.Inward, "PRJ-9"));
        var p2 = Issue("PRJ-2");
        var p9 = Issue("PRJ-9");
        var p10 = Issue("PRJ-10");

        var parents = IssueHierarchy.Compute([child, p2, p9, p10], Rules);

        Assert.Equal(p9.Id, parents[child.Id].ParentId); // Includes важнее Tests, PRJ-9 < PRJ-10
    }

    [Fact]
    public void Cycle_IsBroken()
    {
        var a = Issue("PRJ-1", Link("Includes", LinkDirection.Outward, "PRJ-2"));
        var b = Issue("PRJ-2", Link("Includes", LinkDirection.Outward, "PRJ-1"));

        var parents = IssueHierarchy.Compute([a, b], Rules);

        Assert.Single(parents);
    }

    [Fact]
    public void ParseLinks_ReadsBothDirections()
    {
        var fields = JsonDocument.Parse("""
            { "issuelinks": [
              { "type": { "name": "Tests", "inward": "is tested by", "outward": "tests" }, "inwardIssue": { "key": "TEST-5" } },
              { "type": { "name": "Includes", "inward": "is included in", "outward": "includes" }, "outwardIssue": { "key": "PRJ-2" } },
              { "type": { "name": "Broken" } }
            ] }
            """).RootElement;

        var links = JiraServerClient.ParseLinks(fields);

        Assert.Equal(
        [
            new JiraIssueLink("Tests", LinkDirection.Inward, "TEST-5", "tests", "is tested by"),
            new JiraIssueLink("Includes", LinkDirection.Outward, "PRJ-2", "includes", "is included in"),
        ], links);
    }

    [Fact]
    public void SuggestHierarchy_FindsIncludesAndTestedBy_InEnglishAndRussian()
    {
        var suggested = ConnectionService.SuggestHierarchy(
        [
            new JiraLinkType("Blocks", "blocks", "is blocked by"),
            new JiraLinkType("Тестирование", "тестирует", "тестируется"),
            new JiraLinkType("Включение", "включает в себя", "входит в"),
            new JiraLinkType("Tests", "tests", "is tested by"),
            new JiraLinkType("Includes", "includes", "is included in"),
        ]);

        Assert.Equal(
        [
            new("Включение", LinkDirection.Outward),
            new("Includes", LinkDirection.Outward),
            new("Тестирование", LinkDirection.Inward),
            new("Tests", LinkDirection.Inward),
        ], suggested);
    }

    [Theory]
    [InlineData("Includes|outward", "Includes", LinkDirection.Outward)]
    [InlineData("Is|Tested|inward", "Is|Tested", LinkDirection.Inward)]
    public void HierarchyRule_RoundTrips(string value, string type, LinkDirection side)
    {
        var rule = HierarchyRule.Parse(value)!;
        Assert.Equal(new HierarchyRule(type, side), rule);
        Assert.Equal(value, rule.Serialize());
    }
}
