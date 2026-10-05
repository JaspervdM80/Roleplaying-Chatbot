using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoleplayStudio.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ModelProfileLimitsAndSingleDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ModelId",
                table: "ModelProfiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "BaseUrl",
                table: "ModelProfiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApiKeySetting",
                table: "ModelProfiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.Sql("""
                UPDATE "ModelProfiles" p SET "IsDefault" = false
                WHERE p."IsDefault" AND EXISTS (
                    SELECT 1 FROM "ModelProfiles" q
                    WHERE q."OwnerId" = p."OwnerId" AND q."Role" = p."Role" AND q."IsDefault" AND q."Id" < p."Id")
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ModelProfiles_OneDefaultPerRole",
                table: "ModelProfiles",
                columns: new[] { "OwnerId", "Role" },
                unique: true,
                filter: "\"IsDefault\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModelProfiles_OneDefaultPerRole",
                table: "ModelProfiles");

            migrationBuilder.AlterColumn<string>(
                name: "ModelId",
                table: "ModelProfiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "BaseUrl",
                table: "ModelProfiles",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApiKeySetting",
                table: "ModelProfiles",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
