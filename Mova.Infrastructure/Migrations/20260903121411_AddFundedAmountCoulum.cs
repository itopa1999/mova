using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFundedAmountCoulum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "funded_amount_currency",
                table: "wallets",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "funded_amount_minor_units",
                table: "wallets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "FrequencyConfig",
                table: "wallet_rules",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "funded_amount_currency",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "funded_amount_minor_units",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "FrequencyConfig",
                table: "wallet_rules");
        }
    }
}
