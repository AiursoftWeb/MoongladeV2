using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MoongladeV2.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddDatedPostSlugs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_Slug",
                table: "MarkdownDocuments");

            migrationBuilder.AddColumn<DateTime>(
                name: "SlugDate",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostSlugAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublishedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostSlugAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostSlugAliases_MarkdownDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "MarkdownDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_SlugDate_Slug",
                table: "MarkdownDocuments",
                columns: new[] { "SlugDate", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostSlugAliases_DocumentId",
                table: "PostSlugAliases",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PostSlugAliases_PublishedDate_Slug",
                table: "PostSlugAliases",
                columns: new[] { "PublishedDate", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostSlugAliases");

            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_SlugDate_Slug",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "SlugDate",
                table: "MarkdownDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_Slug",
                table: "MarkdownDocuments",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");
        }
    }
}
