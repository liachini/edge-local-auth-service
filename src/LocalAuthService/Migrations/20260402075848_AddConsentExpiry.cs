using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalAuthService.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "UserConsents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "UserConsents");
        }
    }
}
