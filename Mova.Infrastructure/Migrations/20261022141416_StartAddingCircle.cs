using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StartAddingCircle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SavingsCircleId",
                table: "wallets",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "savings_circles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OrganizerUserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Frequency = table.Column<int>(type: "integer", nullable: false),
                    FrequencyConfig = table.Column<string>(type: "text", nullable: false),
                    MemberCount = table.Column<int>(type: "integer", nullable: false),
                    AllowPenalty = table.Column<bool>(type: "boolean", nullable: false),
                    PenaltyAmountFrom = table.Column<int>(type: "integer", nullable: false),
                    GracePeriodHours = table.Column<int>(type: "integer", nullable: false),
                    RotationStrategy = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartPolicy = table.Column<int>(type: "integer", nullable: false),
                    NextCollectionDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextPayoutDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InviteOnly = table.Column<bool>(type: "boolean", nullable: false),
                    InviteCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PoolBankAccountId = table.Column<long>(type: "bigint", nullable: true),
                    contribution_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    contribution_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    deposit_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    deposit_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    penalty_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    penalty_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_savings_circles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "circle_members",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SavingsCircleId = table.Column<long>(type: "bigint", nullable: false),
                    Identifier = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    UserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InvitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WalletId = table.Column<long>(type: "bigint", nullable: true),
                    RotationPosition = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MissedContributions = table.Column<int>(type: "integer", nullable: false),
                    PayoutReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deposit_held_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    deposit_held_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    total_contributed_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_contributed_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    total_received_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    total_received_minor_units = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_circle_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_circle_members_savings_circles_SavingsCircleId",
                        column: x => x.SavingsCircleId,
                        principalTable: "savings_circles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_circle_members_wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wallets_SavingsCircleId",
                table: "wallets",
                column: "SavingsCircleId");

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_SavingsCircleId_Identifier",
                table: "circle_members",
                columns: new[] { "SavingsCircleId", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_SavingsCircleId_RotationPosition",
                table: "circle_members",
                columns: new[] { "SavingsCircleId", "RotationPosition" },
                unique: true,
                filter: "\"RotationPosition\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_SavingsCircleId_Status",
                table: "circle_members",
                columns: new[] { "SavingsCircleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_UserPublicId",
                table: "circle_members",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_circle_members_WalletId",
                table: "circle_members",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_InviteCode",
                table: "savings_circles",
                column: "InviteCode",
                unique: true,
                filter: "\"InviteCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_OrganizerUserPublicId",
                table: "savings_circles",
                column: "OrganizerUserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_OrganizerUserPublicId_Status",
                table: "savings_circles",
                columns: new[] { "OrganizerUserPublicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_Status",
                table: "savings_circles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_savings_circles_Status_StartDate",
                table: "savings_circles",
                columns: new[] { "Status", "StartDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_wallets_savings_circles_SavingsCircleId",
                table: "wallets",
                column: "SavingsCircleId",
                principalTable: "savings_circles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_wallets_savings_circles_SavingsCircleId",
                table: "wallets");

            migrationBuilder.DropTable(
                name: "circle_members");

            migrationBuilder.DropTable(
                name: "savings_circles");

            migrationBuilder.DropIndex(
                name: "IX_wallets_SavingsCircleId",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "SavingsCircleId",
                table: "wallets");
        }
    }
}
