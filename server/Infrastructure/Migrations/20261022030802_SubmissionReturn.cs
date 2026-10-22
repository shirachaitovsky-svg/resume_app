using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "return_note",
                table: "profile_revisions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "returned_at",
                table: "profile_revisions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "revision_id",
                table: "email_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_outbox_revision_id",
                table: "email_outbox",
                column: "revision_id");

            migrationBuilder.AddForeignKey(
                name: "FK_email_outbox_profile_revisions_revision_id",
                table: "email_outbox",
                column: "revision_id",
                principalTable: "profile_revisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_outbox_profile_revisions_revision_id",
                table: "email_outbox");

            migrationBuilder.DropIndex(
                name: "IX_email_outbox_revision_id",
                table: "email_outbox");

            migrationBuilder.DropColumn(
                name: "return_note",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "returned_at",
                table: "profile_revisions");

            migrationBuilder.DropColumn(
                name: "revision_id",
                table: "email_outbox");
        }
    }
}
