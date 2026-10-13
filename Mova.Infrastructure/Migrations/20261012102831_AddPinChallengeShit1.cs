using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPinChallengeShit1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pin_challenges_ExpiresAt",
                table: "pin_challenges");

            migrationBuilder.DropIndex(
                name: "IX_pin_challenges_UserPublicId",
                table: "pin_challenges");

            migrationBuilder.CreateIndex(
                name: "IX_pin_challenges_UserPublicId_PinChallengeType_IsUsed_Expires~",
                table: "pin_challenges",
                columns: new[] { "UserPublicId", "PinChallengeType", "IsUsed", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pin_challenges_UserPublicId_PinChallengeType_IsUsed_Expires~",
                table: "pin_challenges");

            migrationBuilder.CreateIndex(
                name: "IX_pin_challenges_ExpiresAt",
                table: "pin_challenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_pin_challenges_UserPublicId",
                table: "pin_challenges",
                column: "UserPublicId");
        }
    }
}
