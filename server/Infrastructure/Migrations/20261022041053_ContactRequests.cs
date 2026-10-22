using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContactRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                table: "personal_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                table: "email_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contact_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitor_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    visitor_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_contact_requests_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_personal_tokens_request_id",
                table: "personal_tokens",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "IX_email_outbox_request_id",
                table: "email_outbox",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_requests_status_expires_at",
                table: "contact_requests",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_contact_requests_open_profile_visitor",
                table: "contact_requests",
                columns: new[] { "profile_id", "visitor_email" },
                unique: true,
                filter: "status IN ('awaiting_email_verification', 'submitted', 'sent_to_participant', 'participant_approved')");

            migrationBuilder.AddForeignKey(
                name: "FK_email_outbox_contact_requests_request_id",
                table: "email_outbox",
                column: "request_id",
                principalTable: "contact_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_personal_tokens_contact_requests_request_id",
                table: "personal_tokens",
                column: "request_id",
                principalTable: "contact_requests",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_outbox_contact_requests_request_id",
                table: "email_outbox");

            migrationBuilder.DropForeignKey(
                name: "FK_personal_tokens_contact_requests_request_id",
                table: "personal_tokens");

            migrationBuilder.DropTable(
                name: "contact_requests");

            migrationBuilder.DropIndex(
                name: "ix_personal_tokens_request_id",
                table: "personal_tokens");

            migrationBuilder.DropIndex(
                name: "IX_email_outbox_request_id",
                table: "email_outbox");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "personal_tokens");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "email_outbox");
        }
    }
}
