using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Notification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastKnownDeviceId",
                table: "AspNetUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyLoginAlerts",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyProductUpdates",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyPromotions",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyReleaseAlerts",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastKnownDeviceId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifyLoginAlerts",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifyProductUpdates",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifyPromotions",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NotifyReleaseAlerts",
                table: "AspNetUsers");
        }
    }
}
