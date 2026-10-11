using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoleplayStudio.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeCharacterAgeOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Age",
                table: "Characters",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // A character scene tracking made up got the old default of 12 whenever the story gave no age.
            migrationBuilder.Sql("""UPDATE "Characters" SET "Age" = NULL WHERE "IntroducedInSessionId" IS NOT NULL AND "Age" = 12;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "Characters" SET "Age" = 12 WHERE "Age" IS NULL;""");

            migrationBuilder.AlterColumn<int>(
                name: "Age",
                table: "Characters",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
