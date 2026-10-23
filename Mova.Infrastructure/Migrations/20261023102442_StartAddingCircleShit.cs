using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StartAddingCircleShit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ModeratorStartsCircle",
                table: "savings_circles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "circle_members",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModeratorStartsCircle",
                table: "savings_circles");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "circle_members");
        }
    }
}
