using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RateLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rate_limit_counters",
                columns: table => new
                {
                    scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_limit_counters", x => new { x.scope, x.key_hash, x.window_start });
                });

            migrationBuilder.CreateIndex(
                name: "ix_rate_limit_counters_scope_window",
                table: "rate_limit_counters",
                columns: new[] { "scope", "window_start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rate_limit_counters");
        }
    }
}
