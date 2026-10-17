using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StartedNewImplementationWalletTrustee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "wallets",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "wallet_trustees",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublicId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    WalletId1 = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_trustees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wallet_trustees_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wallet_trustees_wallets_WalletId1",
                        column: x => x.WalletId1,
                        principalTable: "wallets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "release_requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedByUserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Destination = table.Column<int>(type: "integer", nullable: false),
                    DestinationBankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    DestinationWalletId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    UserNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedByTrusteeId = table.Column<long>(type: "bigint", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ApprovalTokenHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    TokenUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TokenUsedFromIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    WalletId1 = table.Column<long>(type: "bigint", nullable: true),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeletedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_release_requests_wallet_trustees_DecidedByTrusteeId",
                        column: x => x.DecidedByTrusteeId,
                        principalTable: "wallet_trustees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_requests_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_requests_wallets_WalletId1",
                        column: x => x.WalletId1,
                        principalTable: "wallets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_wallets_Type_Status",
                table: "wallets",
                columns: new[] { "Type", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_users_bvn_hash",
                table: "AspNetUsers",
                column: "BvnHash");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_ApprovalTokenHash",
                table: "release_requests",
                column: "ApprovalTokenHash",
                unique: true,
                filter: "\"ApprovalTokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_DecidedByTrusteeId",
                table: "release_requests",
                column: "DecidedByTrusteeId");

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_Status_ExpiresAt",
                table: "release_requests",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_WalletId_RequestedAt",
                table: "release_requests",
                columns: new[] { "WalletId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_WalletId_Status_ExpiresAt",
                table: "release_requests",
                columns: new[] { "WalletId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_release_requests_WalletId1",
                table: "release_requests",
                column: "WalletId1");

            migrationBuilder.CreateIndex(
                name: "ux_release_requests_pending_per_wallet",
                table: "release_requests",
                column: "WalletId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_Email",
                table: "wallet_trustees",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_wallet_trustees_PublicId",
                table: "wallet_trustees",
                column: "PublicId",
                unique: true);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "release_requests");

            migrationBuilder.DropTable(
                name: "wallet_trustees");

            migrationBuilder.DropIndex(
                name: "IX_wallets_Type_Status",
                table: "wallets");

            migrationBuilder.DropIndex(
                name: "ix_users_bvn_hash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "wallets");
        }
    }
}
