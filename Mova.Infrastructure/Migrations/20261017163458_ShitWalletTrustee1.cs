using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShitWalletTrustee1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wallet_trustees_PublicId",
                table: "wallet_trustees");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "wallet_trustees");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "wallet_trustees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_PublicId",
                table: "wallet_trustees",
                column: "PublicId",
                unique: true);
        }
    }
}
