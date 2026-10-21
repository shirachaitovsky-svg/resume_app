using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProfileOptionalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_id",
                table: "profile_revisions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "profile_revisions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "intro_answer_1",
                table: "profile_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "intro_answer_2",
                table: "profile_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "intro_answer_3",
                table: "profile_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "intro_text",
                table: "profile_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "photo",
                table: "profile_revisions",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_content_type",
                table: "profile_revisions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "profile_revisions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "workplace",
                table: "profile_revisions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_id",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "description",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "intro_answer_1",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "intro_answer_2",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "intro_answer_3",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "intro_text",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "photo",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "photo_content_type",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "role",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "workplace",
                table: "profile_revisions");
        }
    }
}
