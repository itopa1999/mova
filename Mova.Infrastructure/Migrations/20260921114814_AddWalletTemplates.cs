using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallet_templates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false),
                    DefaultFrequency = table.Column<int>(type: "integer", nullable: false),
                    DefaultFrequencyConfig = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DefaultPayoutDestination = table.Column<int>(type: "integer", nullable: false, defaultValue: 2),
                    IconName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Wallet"),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UsageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    default_release_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    default_release_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
                    default_target_amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    default_target_amount_minor_units = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_wallet_templates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_wallet_templates_wallet_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "wallet_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_templates_CategoryId",
                table: "wallet_templates",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_templates_is_active",
                table: "wallet_templates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "ix_wallet_templates_sort_order",
                table: "wallet_templates",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_templates");
        }
    }
}
