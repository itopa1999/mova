using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Mova.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddedLegal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OnboardingStage",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "legal_documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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
                    table.PrimaryKey("PK_legal_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_consents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserPublicId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LegalDocumentId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentType = table.Column<int>(type: "integer", nullable: false),
                    DocumentVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_user_consents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_consents_legal_documents_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "legal_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_legal_documents_type_is_current",
                table: "legal_documents",
                columns: new[] { "Type", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "ux_legal_documents_one_current_per_type",
                table: "legal_documents",
                column: "Type",
                unique: true,
                filter: "\"IsCurrent\" = true");

            migrationBuilder.CreateIndex(
                name: "ux_legal_documents_type_version",
                table: "legal_documents",
                columns: new[] { "Type", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_document",
                table: "user_consents",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "ix_user_consents_user",
                table: "user_consents",
                column: "UserPublicId");

            migrationBuilder.CreateIndex(
                name: "ux_user_consents_user_document",
                table: "user_consents",
                columns: new[] { "UserPublicId", "LegalDocumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_consents");

            migrationBuilder.DropTable(
                name: "legal_documents");

            migrationBuilder.DropColumn(
                name: "OnboardingStage",
                table: "AspNetUsers");
        }
    }
}
