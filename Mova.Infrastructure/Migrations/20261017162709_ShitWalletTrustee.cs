using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShitWalletTrustee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_wallet_trustees_wallets_WalletId",
                table: "wallet_trustees");

            migrationBuilder.DropForeignKey(
                name: "FK_wallet_trustees_wallets_WalletId1",
                table: "wallet_trustees");

            migrationBuilder.DropIndex(
                name: "IX_wallet_trustees_WalletId",
                table: "wallet_trustees");

            migrationBuilder.DropIndex(
                name: "IX_wallet_trustees_WalletId1",
                table: "wallet_trustees");

            migrationBuilder.DropColumn(
                name: "WalletId",
                table: "wallet_trustees");

            migrationBuilder.DropColumn(
                name: "WalletId1",
                table: "wallet_trustees");

            migrationBuilder.AddColumn<long>(
                name: "WalletTrusteeId",
                table: "wallets",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentedFromIp",
                table: "wallet_trustees",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvitedAt",
                table: "wallet_trustees",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "wallet_trustees",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TokenExpiresAt",
                table: "wallet_trustees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "VerificationTokenHash",
                table: "wallet_trustees",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VerifiedAt",
                table: "wallet_trustees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallets_WalletTrusteeId",
                table: "wallets",
                column: "WalletTrusteeId");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_Status_TokenExpiresAt",
                table: "wallet_trustees",
                columns: new[] { "Status", "TokenExpiresAt" },
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_VerificationTokenHash",
                table: "wallet_trustees",
                column: "VerificationTokenHash",
                unique: true,
                filter: "\"VerificationTokenHash\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_wallets_wallet_trustees_WalletTrusteeId",
                table: "wallets",
                column: "WalletTrusteeId",
                principalTable: "wallet_trustees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_wallets_wallet_trustees_WalletTrusteeId",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "IX_wallets_WalletTrusteeId",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "IX_wallet_trustees_Status_TokenExpiresAt",
                table: "wallet_trustees");

            migrationBuilder.DropIndex(
                name: "IX_wallet_trustees_VerificationTokenHash",
                table: "wallet_trustees");

            migrationBuilder.DropColumn(
                name: "WalletTrusteeId",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "ConsentedFromIp",
                table: "wallet_trustees");

            migrationBuilder.DropColumn(
                name: "InvitedAt",
                table: "wallet_trustees");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "wallet_trustees");

            migrationBuilder.DropColumn(
                name: "TokenExpiresAt",
                table: "wallet_trustees");

            migrationBuilder.DropColumn(
                name: "VerificationTokenHash",
                table: "wallet_trustees");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "wallet_trustees");

            migrationBuilder.AddColumn<long>(
                name: "WalletId",
                table: "wallet_trustees",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "WalletId1",
                table: "wallet_trustees",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_WalletId",
                table: "wallet_trustees",
                column: "WalletId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_WalletId1",
                table: "wallet_trustees",
                column: "WalletId1",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_wallet_trustees_wallets_WalletId",
                table: "wallet_trustees",
                column: "WalletId",
                principalTable: "wallets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_wallet_trustees_wallets_WalletId1",
                table: "wallet_trustees",
                column: "WalletId1",
                principalTable: "wallets",
                principalColumn: "Id");
        }
    }
}
