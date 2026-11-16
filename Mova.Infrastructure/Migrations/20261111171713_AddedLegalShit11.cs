using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddedLegalShit11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "request_profile_changes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NewFirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewOtherName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewLastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NewEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NewPhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RequestReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RequestComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OldFirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OldOtherName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OldLastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OldEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    OldPhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsRejected = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RejectedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_request_profile_changes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_request_profile_changes_IsApproved",
                table: "request_profile_changes",
                column: "IsApproved");

            migrationBuilder.CreateIndex(
                name: "IX_request_profile_changes_IsApproved_IsRejected",
                table: "request_profile_changes",
                columns: new[] { "IsApproved", "IsRejected" });

            migrationBuilder.CreateIndex(
                name: "IX_request_profile_changes_UserPublicId",
                table: "request_profile_changes",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "IX_request_profile_changes_UserPublicId_CreatedAt",
                table: "request_profile_changes",
                columns: new[] { "UserPublicId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "request_profile_changes");
        }
    }
}
