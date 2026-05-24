using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MoongladeV2.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAiEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Embedding",
                table: "MarkdownDocuments",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeroImageUrl",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFeatured",
                table: "MarkdownDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEmbeddedAt",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "MarkdownDocuments",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "LocalizedDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Culture = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LocalizedTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LocalizedContent = table.Column<string>(type: "TEXT", maxLength: 65535, nullable: false),
                    LastLocalizedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalizedDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocalizedDocuments_MarkdownDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "MarkdownDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SearchEmbeddings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QueryText = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAccessedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchEmbeddings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_Slug",
                table: "MarkdownDocuments",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LocalizedDocuments_DocumentId_Culture",
                table: "LocalizedDocuments",
                columns: new[] { "DocumentId", "Culture" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchEmbeddings_QueryText",
                table: "SearchEmbeddings",
                column: "QueryText",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocalizedDocuments");

            migrationBuilder.DropTable(
                name: "SearchEmbeddings");

            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_Slug",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "HeroImageUrl",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "IsFeatured",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "LastEmbeddedAt",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "MarkdownDocuments");
        }
    }
}
