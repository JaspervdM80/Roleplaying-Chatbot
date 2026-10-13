using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoleplayStudio.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class CharacterStateRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Relationship",
                table: "CharacterStates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Relationship",
                table: "CharacterStates");
        }
    }
}
