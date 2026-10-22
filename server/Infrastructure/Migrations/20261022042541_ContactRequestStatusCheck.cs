using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContactRequestStatusCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_contact_requests_status",
                table: "contact_requests",
                sql: "status IN ('awaiting_email_verification', 'submitted', 'sent_to_participant', 'participant_approved', 'participant_declined', 'delivered', 'expired')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contact_requests_status",
                table: "contact_requests");
        }
    }
}
