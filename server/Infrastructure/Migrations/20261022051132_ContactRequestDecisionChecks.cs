using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContactRequestDecisionChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_contact_requests_decision_pair",
                table: "contact_requests",
                sql: "(participant_decision IS NULL) = (participant_decided_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contact_requests_manager_approval",
                table: "contact_requests",
                sql: "manager_approved_at IS NULL OR COALESCE(participant_decision, '') = 'approved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contact_requests_decision_pair",
                table: "contact_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contact_requests_manager_approval",
                table: "contact_requests");
        }
    }
}
