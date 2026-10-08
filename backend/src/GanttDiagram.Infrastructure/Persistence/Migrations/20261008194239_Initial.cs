using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GanttDiagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "jira_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EncryptedToken = table.Column<string>(type: "text", nullable: false),
                    ProjectKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartDateFieldId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EndDateFieldId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HoursPerDay = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jira_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "epics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_epics_jira_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "jira_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jira_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    JiraKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jira_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_jira_users_jira_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "jira_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_projects_jira_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "jira_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpicId = table.Column<Guid>(type: "uuid", nullable: true),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StatusCategory = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OriginalEstimateSeconds = table.Column<long>(type: "bigint", nullable: true),
                    JiraStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    JiraEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    JiraAssigneeId = table.Column<Guid>(type: "uuid", nullable: true),
                    JiraUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlanStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PlanEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PlanAssigneeId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsLocallyModified = table.Column<bool>(type: "boolean", nullable: false),
                    LocalModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_issues_epics_EpicId",
                        column: x => x.EpicId,
                        principalTable: "epics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_issues_jira_connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "jira_connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_issues_jira_users_JiraAssigneeId",
                        column: x => x.JiraAssigneeId,
                        principalTable: "jira_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_issues_jira_users_PlanAssigneeId",
                        column: x => x.PlanAssigneeId,
                        principalTable: "jira_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "project_epics",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EpicId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_epics", x => new { x.ProjectId, x.EpicId });
                    table.ForeignKey(
                        name: "FK_project_epics_epics_EpicId",
                        column: x => x.EpicId,
                        principalTable: "epics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_epics_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_members",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    JiraUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_members", x => new { x.ProjectId, x.JiraUserId });
                    table.ForeignKey(
                        name: "FK_project_members_jira_users_JiraUserId",
                        column: x => x.JiraUserId,
                        principalTable: "jira_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_members_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_issues",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_issues", x => new { x.ProjectId, x.IssueId });
                    table.ForeignKey(
                        name: "FK_project_issues_issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_issues_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_epics_ConnectionId_Key",
                table: "epics",
                columns: new[] { "ConnectionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issues_ConnectionId_Key",
                table: "issues",
                columns: new[] { "ConnectionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_issues_EpicId",
                table: "issues",
                column: "EpicId");

            migrationBuilder.CreateIndex(
                name: "IX_issues_JiraAssigneeId",
                table: "issues",
                column: "JiraAssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_issues_PlanAssigneeId",
                table: "issues",
                column: "PlanAssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_jira_users_ConnectionId_Name",
                table: "jira_users",
                columns: new[] { "ConnectionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_epics_EpicId",
                table: "project_epics",
                column: "EpicId");

            migrationBuilder.CreateIndex(
                name: "IX_project_issues_IssueId",
                table: "project_issues",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_project_members_JiraUserId",
                table: "project_members",
                column: "JiraUserId");

            migrationBuilder.CreateIndex(
                name: "IX_projects_ConnectionId",
                table: "projects",
                column: "ConnectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_epics");

            migrationBuilder.DropTable(
                name: "project_issues");

            migrationBuilder.DropTable(
                name: "project_members");

            migrationBuilder.DropTable(
                name: "issues");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "epics");

            migrationBuilder.DropTable(
                name: "jira_users");

            migrationBuilder.DropTable(
                name: "jira_connections");
        }
    }
}
