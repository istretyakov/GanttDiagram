using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GanttDiagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IssueHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "HierarchyLinks",
                table: "jira_connections",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.CreateTable(
                name: "issue_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkTypeName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LinkedIssueKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OutwardVerb = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    InwardVerb = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_issue_links_issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_issue_links_IssueId",
                table: "issue_links",
                column: "IssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_links");

            migrationBuilder.DropColumn(
                name: "HierarchyLinks",
                table: "jira_connections");
        }
    }
}
