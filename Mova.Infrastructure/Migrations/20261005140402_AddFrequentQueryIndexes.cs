using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFrequentQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_scheduled_releases_Status_ScheduledFor",
                table: "scheduled_releases");

            migrationBuilder.DropIndex(
                name: "IX_scheduled_releases_WalletRuleId",
                table: "scheduled_releases");

            migrationBuilder.DropIndex(
                name: "IX_scheduled_releases_WalletRuleId_ScheduledFor",
                table: "scheduled_releases");

            migrationBuilder.DropIndex(
                name: "IX_notifications_CreatedAt",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_UserPublicId",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_UserPublicId_IsRead",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ProcessingQueue",
                table: "transactions",
                column: "Id",
                filter: "\"IsDeleted\" = FALSE AND (\"Status\" = 1 OR \"Status\" = 2)");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_UserPublicId_CreatedAt_Id",
                table: "transactions",
                columns: new[] { "UserPublicId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_releases_Status_ScheduledFor_Id",
                table: "scheduled_releases",
                columns: new[] { "Status", "ScheduledFor", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_releases_WalletRuleId_ScheduledFor_Status",
                table: "scheduled_releases",
                columns: new[] { "WalletRuleId", "ScheduledFor", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_payouts_ProcessingQueue",
                table: "payouts",
                column: "Id",
                filter: "\"IsDeleted\" = FALSE AND (\"Status\" = 1 OR \"Status\" = 2)");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserPublicId_CreatedAt",
                table: "notifications",
                columns: new[] { "UserPublicId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserPublicId_IsRead_CreatedAt",
                table: "notifications",
                columns: new[] { "UserPublicId", "IsRead", "CreatedAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_ProcessingQueue",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_UserPublicId_CreatedAt_Id",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_scheduled_releases_Status_ScheduledFor_Id",
                table: "scheduled_releases");

            migrationBuilder.DropIndex(
                name: "IX_scheduled_releases_WalletRuleId_ScheduledFor_Status",
                table: "scheduled_releases");

            migrationBuilder.DropIndex(
                name: "IX_payouts_ProcessingQueue",
                table: "payouts");

            migrationBuilder.DropIndex(
                name: "IX_notifications_UserPublicId_CreatedAt",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_notifications_UserPublicId_IsRead_CreatedAt",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_releases_Status_ScheduledFor",
                table: "scheduled_releases",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_releases_WalletRuleId",
                table: "scheduled_releases",
                column: "WalletRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_scheduled_releases_WalletRuleId_ScheduledFor",
                table: "scheduled_releases",
                columns: new[] { "WalletRuleId", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_CreatedAt",
                table: "notifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserPublicId",
                table: "notifications",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserPublicId_IsRead",
                table: "notifications",
                columns: new[] { "UserPublicId", "IsRead" });
        }
    }
}
