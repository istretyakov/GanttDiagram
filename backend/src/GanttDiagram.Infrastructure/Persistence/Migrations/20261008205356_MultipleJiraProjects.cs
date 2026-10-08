using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GanttDiagram.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultipleJiraProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "ProjectKeys",
                table: "jira_connections",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            // Один ключ проекта превращается в список из одного элемента.
            migrationBuilder.Sql("""UPDATE jira_connections SET "ProjectKeys" = ARRAY["ProjectKey"]::text[];""");

            migrationBuilder.DropColumn(
                name: "ProjectKey",
                table: "jira_connections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProjectKey",
                table: "jira_connections",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""UPDATE jira_connections SET "ProjectKey" = COALESCE("ProjectKeys"[1], '');""");

            migrationBuilder.DropColumn(
                name: "ProjectKeys",
                table: "jira_connections");
        }
    }
}
