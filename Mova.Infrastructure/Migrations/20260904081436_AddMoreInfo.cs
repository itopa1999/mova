using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMoreInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "total_released_amount_currency",
                table: "wallets",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "total_released_amount_minor_units",
                table: "wallets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "total_withdrawn_amount_currency",
                table: "wallets",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "total_withdrawn_amount_minor_units",
                table: "wallets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "balance_currency",
                table: "AspNetUsers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "balance_minor_units",
                table: "AspNetUsers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "total_released_amount_currency",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "total_released_amount_minor_units",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "total_withdrawn_amount_currency",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "total_withdrawn_amount_minor_units",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "balance_currency",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "balance_minor_units",
                table: "AspNetUsers");
        }
    }
}
