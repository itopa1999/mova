using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StartAddingCircleShit1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "circle_rounds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SavingsCircleId = table.Column<long>(type: "bigint", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    CollectionDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayoutDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecipientUserPublicId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PaidOutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayoutTransactionReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequiresManualReview = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    paid_out_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    paid_out_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    pooled_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    pooled_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_circle_rounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_circle_rounds_savings_circles_SavingsCircleId",
                        column: x => x.SavingsCircleId,
                        principalTable: "savings_circles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contributions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CircleRoundId = table.Column<long>(type: "bigint", nullable: false),
                    CircleMemberId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CollectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    penalty_applied_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    penalty_applied_minor_units = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_contributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contributions_circle_members_CircleMemberId",
                        column: x => x.CircleMemberId,
                        principalTable: "circle_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contributions_circle_rounds_CircleRoundId",
                        column: x => x.CircleRoundId,
                        principalTable: "circle_rounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_circle_rounds_circle_round_number",
                table: "circle_rounds",
                columns: new[] { "SavingsCircleId", "RoundNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_circle_rounds_manual_review",
                table: "circle_rounds",
                column: "RequiresManualReview",
                filter: "\"RequiresManualReview\" = true");

            migrationBuilder.CreateIndex(
                name: "ix_circle_rounds_recipient",
                table: "circle_rounds",
                column: "RecipientUserPublicId");

            migrationBuilder.CreateIndex(
                name: "ix_circle_rounds_status_payout_date",
                table: "circle_rounds",
                columns: new[] { "Status", "PayoutDate" });

            migrationBuilder.CreateIndex(
                name: "ix_contributions_member",
                table: "contributions",
                column: "CircleMemberId");

            migrationBuilder.CreateIndex(
                name: "ix_contributions_payment_reference",
                table: "contributions",
                column: "PaymentReference",
                unique: true,
                filter: "\"PaymentReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contributions_round_member",
                table: "contributions",
                columns: new[] { "CircleRoundId", "CircleMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contributions_status",
                table: "contributions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contributions");

            migrationBuilder.DropTable(
                name: "circle_rounds");
        }
    }
}
