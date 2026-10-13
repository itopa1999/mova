using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnotherShitAgain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UtilityConfigJson",
                table: "wallets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UtilityType",
                table: "wallets",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UtilityConfigJson",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "UtilityType",
                table: "wallets");
        }
    }
}
