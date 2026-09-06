using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payouts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    WalletId = table.Column<string>(type: "text", nullable: false),
                    BankAccountId = table.Column<string>(type: "text", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InitiatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    fee_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    net_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    net_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_payouts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payouts_ProviderReference",
                table: "payouts",
                column: "ProviderReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payouts_UserPublicId",
                table: "payouts",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_payouts_WalletId_Status",
                table: "payouts",
                columns: new[] { "WalletId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payouts");
        }
    }
}
