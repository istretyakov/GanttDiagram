using GanttDiagram.Application.Abstractions;
using GanttDiagram.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GanttDiagram.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<JiraConnection> Connections => Set<JiraConnection>();
    public DbSet<JiraUser> Users => Set<JiraUser>();
    public DbSet<Epic> Epics => Set<Epic>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectEpic> ProjectEpics => Set<ProjectEpic>();
    public DbSet<ProjectIssue> ProjectIssues => Set<ProjectIssue>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<IssueLink> IssueLinks => Set<IssueLink>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<JiraConnection>(e =>
        {
            e.ToTable("jira_connections");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.BaseUrl).HasMaxLength(500);
            e.Property(x => x.ProjectKeys).HasColumnType("text[]");
            e.Property(x => x.HierarchyLinks).HasColumnType("text[]");
            e.Ignore(x => x.HierarchyRules);
            e.Property(x => x.StartDateFieldId).HasMaxLength(100);
            e.Property(x => x.EndDateFieldId).HasMaxLength(100);
        });

        b.Entity<JiraUser>(e =>
        {
            e.ToTable("jira_users");
            e.HasIndex(x => new { x.ConnectionId, x.Name }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(255);
            e.Property(x => x.JiraKey).HasMaxLength(255);
            e.Property(x => x.DisplayName).HasMaxLength(255);
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.AvatarUrl).HasMaxLength(1000);
            e.HasOne(x => x.Connection).WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Epic>(e =>
        {
            e.ToTable("epics");
            e.HasIndex(x => new { x.ConnectionId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(50);
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.HasOne(x => x.Connection).WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Issue>(e =>
        {
            e.ToTable("issues");
            e.HasIndex(x => new { x.ConnectionId, x.Key }).IsUnique();
            e.HasIndex(x => x.PlanAssigneeId);
            e.Property(x => x.Key).HasMaxLength(50);
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.Property(x => x.Status).HasMaxLength(100);
            e.Property(x => x.StatusCategory).HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion(); // xmin
            e.Ignore(x => x.DiffersFromJira);
            e.Ignore(x => x.IsScheduled);
            e.HasOne(x => x.Connection).WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Epic).WithMany().HasForeignKey(x => x.EpicId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.JiraAssignee).WithMany().HasForeignKey(x => x.JiraAssigneeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.PlanAssignee).WithMany().HasForeignKey(x => x.PlanAssigneeId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<IssueLink>(e =>
        {
            e.ToTable("issue_links");
            e.HasIndex(x => x.IssueId);
            e.Property(x => x.LinkTypeName).HasMaxLength(255);
            e.Property(x => x.LinkedIssueKey).HasMaxLength(50);
            e.Property(x => x.OutwardVerb).HasMaxLength(255);
            e.Property(x => x.InwardVerb).HasMaxLength(255);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
            e.HasOne(x => x.Issue).WithMany(i => i.Links).HasForeignKey(x => x.IssueId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasOne(x => x.Connection).WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProjectEpic>(e =>
        {
            e.ToTable("project_epics");
            e.HasKey(x => new { x.ProjectId, x.EpicId });
            e.HasOne(x => x.Project).WithMany(p => p.Epics).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Epic).WithMany().HasForeignKey(x => x.EpicId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProjectIssue>(e =>
        {
            e.ToTable("project_issues");
            e.HasKey(x => new { x.ProjectId, x.IssueId });
            e.HasIndex(x => x.IssueId);
            e.HasOne(x => x.Project).WithMany(p => p.Issues).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Issue).WithMany().HasForeignKey(x => x.IssueId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProjectMember>(e =>
        {
            e.ToTable("project_members");
            e.HasKey(x => new { x.ProjectId, x.JiraUserId });
            e.HasOne(x => x.Project).WithMany(p => p.Members).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.JiraUser).WithMany().HasForeignKey(x => x.JiraUserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
