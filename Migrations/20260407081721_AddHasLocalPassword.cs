using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LocalAuthService.Migrations
{
    /// <inheritdoc />
    public partial class AddHasLocalPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasLocalPassword",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasLocalPassword",
                table: "Users");
        }
    }
}
