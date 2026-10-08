using GanttDiagram.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GanttDiagram.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<JiraConnection> Connections { get; }
    DbSet<JiraUser> Users { get; }
    DbSet<Epic> Epics { get; }
    DbSet<Issue> Issues { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectEpic> ProjectEpics { get; }
    DbSet<ProjectIssue> ProjectIssues { get; }
    DbSet<ProjectMember> ProjectMembers { get; }
    DbSet<IssueLink> IssueLinks { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
