using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalAuthService.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowedClientIdsToLegacyCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedClientIds",
                table: "LegacyServiceCredentials",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedClientIds",
                table: "LegacyServiceCredentials");
        }
    }
}
