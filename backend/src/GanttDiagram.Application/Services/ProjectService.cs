using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Dtos;
using GanttDiagram.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GanttDiagram.Application.Services;

public class ProjectService(IAppDbContext db, ImportService importService, TimeProvider time)
{
    public async Task<IReadOnlyList<ProjectSummaryDto>> ListAsync(CancellationToken ct) =>
        await db.Projects
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProjectSummaryDto(
                p.Id, p.Name, p.Description, p.ConnectionId, p.Connection!.Name, p.CreatedAt,
                p.Issues.Count, p.Members.Count, p.Epics.Count))
            .ToListAsync(ct);

    public async Task<ProjectDto> GetAsync(Guid id, CancellationToken ct)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Connection)
            .Include(p => p.Epics).ThenInclude(pe => pe.Epic)
            .Include(p => p.Members).ThenInclude(m => m.JiraUser)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw NotFoundException.For("Проект", id);

        var issues = await LoadIssuesAsync(id, ct);
        var parents = IssueHierarchy.Compute(issues, project.Connection!.HierarchyRules);
        return new ProjectDto(
            project.Id, project.Name, project.Description, project.ConnectionId,
            project.Connection!.Name, project.Connection.BaseUrl, project.CreatedAt,
            project.Epics.Select(pe => pe.Epic!).OrderBy(e => e.Key).Select(e => e.ToDto()).ToList(),
            project.Members.OrderBy(m => m.SortOrder).Select(m => m.ToDto()).ToList(),
            issues.Select(i => i.ToDto(project.Connection.BaseUrl, parents.GetValueOrDefault(i.Id))).ToList());
    }

    public async Task<ProjectSummaryDto> CreateAsync(ProjectInput input, CancellationToken ct)
    {
        Validate(input);
        var connection = await db.Connections.FirstOrDefaultAsync(c => c.Id == input.ConnectionId, ct)
                         ?? throw new ValidationFailedException("Подключение Jira не найдено.",
                             new Dictionary<string, string[]> { ["connectionId"] = ["Выберите подключение."] });
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = input.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            ConnectionId = connection.Id,
            CreatedAt = time.GetUtcNow(),
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return new ProjectSummaryDto(project.Id, project.Name, project.Description, connection.Id, connection.Name,
            project.CreatedAt, 0, 0, 0);
    }

    public async Task UpdateAsync(Guid id, ProjectInput input, CancellationToken ct)
    {
        Validate(input);
        var project = await FindAsync(id, ct);
        if (project.ConnectionId != input.ConnectionId)
            throw new ValidationFailedException("Подключение проекта менять нельзя.");
        project.Name = input.Name.Trim();
        project.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Копия проекта: эпики, задачи и участники (с порядком строк) — по выбору.</summary>
    public async Task<ProjectSummaryDto> CopyAsync(Guid id, CopyProjectRequest request, CancellationToken ct)
    {
        var source = await db.Projects
            .AsNoTracking()
            .Include(p => p.Connection)
            .Include(p => p.Epics)
            .Include(p => p.Issues)
            .Include(p => p.Members)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw NotFoundException.For("Проект", id);

        var copy = new Project
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"{source.Name} (копия)" : request.Name.Trim(),
            Description = request.Description is null ? source.Description
                : string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            ConnectionId = source.ConnectionId,
            CreatedAt = time.GetUtcNow(),
        };
        if (request.IncludeEpics)
            copy.Epics = source.Epics.Select(e => new ProjectEpic { ProjectId = copy.Id, EpicId = e.EpicId }).ToList();
        if (request.IncludeIssues)
            copy.Issues = source.Issues.Select(i => new ProjectIssue { ProjectId = copy.Id, IssueId = i.IssueId }).ToList();
        if (request.IncludeMembers)
            copy.Members = source.Members
                .Select(m => new ProjectMember { ProjectId = copy.Id, JiraUserId = m.JiraUserId, SortOrder = m.SortOrder })
                .ToList();

        db.Projects.Add(copy);
        await db.SaveChangesAsync(ct);
        return new ProjectSummaryDto(copy.Id, copy.Name, copy.Description, copy.ConnectionId, source.Connection!.Name,
            copy.CreatedAt, copy.Issues.Count, copy.Members.Count, copy.Epics.Count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var project = await FindAsync(id, ct);
        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Привязать эпики к проекту и импортировать их задачи.</summary>
    public async Task<ImportSummary> AttachEpicsAsync(Guid id, AttachEpicsRequest request, CancellationToken ct)
    {
        var project = await FindAsync(id, ct);
        return await importService.ImportAsync(project.ConnectionId, request.EpicKeys, id, ct);
    }

    /// <summary>Привязать отдельные, уже импортированные задачи по ключам.</summary>
    public async Task<AttachIssuesResult> AttachIssuesAsync(Guid id, AttachIssuesRequest request, CancellationToken ct)
    {
        var project = await FindAsync(id, ct);
        var keys = request.IssueKeys.Select(k => k.Trim().ToUpperInvariant()).Where(k => k.Length > 0).Distinct().ToList();
        var issues = await db.Issues
            .Where(i => i.ConnectionId == project.ConnectionId && keys.Contains(i.Key))
            .Select(i => new { i.Id, i.Key })
            .ToListAsync(ct);
        var linked = await db.ProjectIssues.Where(pi => pi.ProjectId == id).Select(pi => pi.IssueId).ToListAsync(ct);

        var attached = 0;
        foreach (var issue in issues.Where(i => !linked.Contains(i.Id)))
        {
            db.ProjectIssues.Add(new ProjectIssue { ProjectId = id, IssueId = issue.Id });
            attached++;
        }
        await db.SaveChangesAsync(ct);
        return new AttachIssuesResult(attached, keys.Except(issues.Select(i => i.Key)).ToList());
    }

    public async Task DetachIssueAsync(Guid id, Guid issueId, CancellationToken ct)
    {
        var link = await db.ProjectIssues.FirstOrDefaultAsync(pi => pi.ProjectId == id && pi.IssueId == issueId, ct)
                   ?? throw NotFoundException.For("Задача проекта", issueId);
        db.ProjectIssues.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Кнопка «+»: добавить участника из синхронизированных пользователей Jira.</summary>
    public async Task<MemberDto> AddMemberAsync(Guid id, AddMemberRequest request, CancellationToken ct)
    {
        var project = await FindAsync(id, ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.JiraUserId, ct)
                   ?? throw NotFoundException.For("Пользователь", request.JiraUserId);
        if (user.ConnectionId != project.ConnectionId)
            throw new ValidationFailedException("Пользователь относится к другому подключению Jira.");

        var members = await db.ProjectMembers.Where(m => m.ProjectId == id).ToListAsync(ct);
        var existing = members.FirstOrDefault(m => m.JiraUserId == user.Id);
        if (existing is not null)
        {
            existing.JiraUser = user;
            return existing.ToDto();
        }

        var member = new ProjectMember
        {
            ProjectId = id,
            JiraUserId = user.Id,
            JiraUser = user,
            SortOrder = members.Count == 0 ? 0 : members.Max(m => m.SortOrder) + 1,
        };
        db.ProjectMembers.Add(member);
        await db.SaveChangesAsync(ct);
        return member.ToDto();
    }

    /// <summary>Удалить строку участника — только если у него нет задач в этом проекте.</summary>
    public async Task RemoveMemberAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var member = await db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == id && m.JiraUserId == userId, ct)
                     ?? throw NotFoundException.For("Участник", userId);
        var hasIssues = await db.ProjectIssues
            .AnyAsync(pi => pi.ProjectId == id && pi.Issue!.PlanAssigneeId == userId, ct);
        if (hasIssues)
            throw new ValidationFailedException("У участника есть задачи в этом проекте. Сначала переназначьте их.");
        db.ProjectMembers.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderMembersAsync(Guid id, MembersOrderRequest request, CancellationToken ct)
    {
        await FindAsync(id, ct);
        var members = await db.ProjectMembers.Where(m => m.ProjectId == id).ToListAsync(ct);
        var order = request.UserIds.Select((userId, index) => (userId, index)).ToDictionary(x => x.userId, x => x.index);
        var tail = order.Count;
        foreach (var member in members.OrderBy(m => m.SortOrder))
        {
            member.SortOrder = order.TryGetValue(member.JiraUserId, out var index) ? index : tail++;
        }
        await db.SaveChangesAsync(ct);
    }

    internal async Task<List<Issue>> LoadIssuesAsync(Guid projectId, CancellationToken ct) =>
        await db.Issues
            .AsNoTracking()
            .Where(i => db.ProjectIssues.Any(pi => pi.ProjectId == projectId && pi.IssueId == i.Id))
            .Include(i => i.Epic)
            .Include(i => i.PlanAssignee)
            .Include(i => i.JiraAssignee)
            .Include(i => i.Links)
            .AsSplitQuery()
            .OrderBy(i => i.Key)
            .ToListAsync(ct);

    private async Task<Project> FindAsync(Guid id, CancellationToken ct) =>
        await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFoundException.For("Проект", id);

    private static void Validate(ProjectInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ValidationFailedException("Укажите название проекта.",
                new Dictionary<string, string[]> { ["name"] = ["Укажите название проекта."] });
    }
}
