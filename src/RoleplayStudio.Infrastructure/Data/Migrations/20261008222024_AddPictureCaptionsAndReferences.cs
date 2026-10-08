using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoleplayStudio.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPictureCaptionsAndReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AvatarImageId",
                table: "Characters");

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsReferenceImage",
                table: "ModelProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Caption",
                table: "Images",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptsReferenceImage",
                table: "ModelProfiles");

            migrationBuilder.DropColumn(
                name: "Caption",
                table: "Images");

            migrationBuilder.AddColumn<Guid>(
                name: "ImageId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AvatarImageId",
                table: "Characters",
                type: "uuid",
                nullable: true);
        }
    }
}
