using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoleplayStudio.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackIntroducedCharactersAndPictureReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "ReferenceImageIds",
                table: "Images",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<Guid>(
                name: "IntroducedInSessionId",
                table: "Characters",
                type: "uuid",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "AcceptsReferenceImage",
                table: "ModelProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferenceImageIds",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "IntroducedInSessionId",
                table: "Characters");

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsReferenceImage",
                table: "ModelProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
