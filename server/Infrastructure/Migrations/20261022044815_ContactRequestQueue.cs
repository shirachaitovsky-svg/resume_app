using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContactRequestQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "manager_approved_at",
                table: "contact_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "participant_decided_at",
                table: "contact_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "participant_decision",
                table: "contact_requests",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contact_requests_queue_open",
                table: "contact_requests",
                columns: new[] { "verified_at", "id" },
                filter: "status IN ('submitted', 'sent_to_participant', 'participant_approved')");

            migrationBuilder.CreateIndex(
                name: "ix_contact_requests_verified",
                table: "contact_requests",
                columns: new[] { "verified_at", "status", "id" },
                filter: "verified_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contact_requests_participant_decision",
                table: "contact_requests",
                sql: "participant_decision IN ('approved', 'declined')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_contact_requests_queue_open",
                table: "contact_requests");

            migrationBuilder.DropIndex(
                name: "ix_contact_requests_verified",
                table: "contact_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contact_requests_participant_decision",
                table: "contact_requests");

            migrationBuilder.DropColumn(
                name: "manager_approved_at",
                table: "contact_requests");

            migrationBuilder.DropColumn(
                name: "participant_decided_at",
                table: "contact_requests");

            migrationBuilder.DropColumn(
                name: "participant_decision",
                table: "contact_requests");
        }
    }
}
