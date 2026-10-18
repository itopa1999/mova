using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WalletTypeTemplatesadded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WalletType",
                table: "wallet_templates",
                type: "integer",
                nullable: true,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_wallet_templates_type_active",
                table: "wallet_templates",
                columns: new[] { "WalletType", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wallet_templates_type_active",
                table: "wallet_templates");

            migrationBuilder.DropColumn(
                name: "WalletType",
                table: "wallet_templates");
        }
    }
}
