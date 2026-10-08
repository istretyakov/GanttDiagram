using GanttDiagram.Domain.Entities;

namespace GanttDiagram.Application.Services;

/// <summary>Родитель задачи в иерархии проекта и глагол связи («includes», «is tested by»).</summary>
public record IssueParent(Guid ParentId, string Verb);

/// <summary>
/// Иерархия задач по связям Jira (issue links) и правилам подключения.
/// Строится только внутри переданного набора задач (задачи проекта).
/// </summary>
public static class IssueHierarchy
{
    public static Dictionary<Guid, IssueParent> Compute(IReadOnlyCollection<Issue> issues, IReadOnlyList<HierarchyRule> rules)
    {
        var result = new Dictionary<Guid, IssueParent>();
        if (rules.Count == 0 || issues.Count == 0) return result;

        var byKey = issues.GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Кандидаты в родители: (дочерняя, родитель, приоритет правила, глагол). Связь видна с обеих сторон.
        var candidates = new List<(Issue Child, Issue Parent, int Rule, string Verb)>();
        foreach (var issue in issues)
        {
            foreach (var link in issue.Links)
            {
                if (!byKey.TryGetValue(link.LinkedIssueKey, out var other) || other.Id == issue.Id) continue;
                for (var r = 0; r < rules.Count; r++)
                {
                    var rule = rules[r];
                    var verb = rule.Verb(link.OutwardVerb, link.InwardVerb);
                    if (rule.LinkedIsParent(link.LinkTypeName, link.Direction)) candidates.Add((issue, other, r, verb));
                    else if (rule.LinkedIsChild(link.LinkTypeName, link.Direction)) candidates.Add((other, issue, r, verb));
                }
            }
        }

        // У задачи один родитель: первое правило, затем меньший ключ. Циклы отбрасываются.
        foreach (var group in candidates.GroupBy(c => c.Child.Id).OrderBy(g => g.First().Child.Key, KeyComparer.Instance))
        {
            foreach (var (child, parent, _, verb) in group.OrderBy(c => c.Rule).ThenBy(c => c.Parent.Key, KeyComparer.Instance))
            {
                if (CreatesCycle(child.Id, parent.Id, result)) continue;
                result[child.Id] = new IssueParent(parent.Id, verb);
                break;
            }
        }
        return result;
    }

    private static bool CreatesCycle(Guid child, Guid parent, Dictionary<Guid, IssueParent> parents)
    {
        for (Guid? current = parent; current is { } id; current = parents.GetValueOrDefault(id)?.ParentId)
        {
            if (id == child) return true;
        }
        return false;
    }

    /// <summary>PRJ-2 &lt; PRJ-10.</summary>
    public sealed class KeyComparer : IComparer<string>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var (px, nx) = Split(x ?? "");
            var (py, ny) = Split(y ?? "");
            var byPrefix = string.Compare(px, py, StringComparison.OrdinalIgnoreCase);
            return byPrefix != 0 ? byPrefix : nx.CompareTo(ny);
        }

        private static (string, long) Split(string key)
        {
            var i = key.LastIndexOf('-');
            return i > 0 && long.TryParse(key[(i + 1)..], out var n) ? (key[..i], n) : (key, 0);
        }
    }
}
