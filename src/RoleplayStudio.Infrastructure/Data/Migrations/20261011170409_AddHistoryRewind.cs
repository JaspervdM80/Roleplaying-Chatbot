using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoleplayStudio.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryRewind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SceneBefore",
                table: "Messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HistoryVersion",
                table: "ChatSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SceneBefore",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "HistoryVersion",
                table: "ChatSessions");
        }
    }
}
