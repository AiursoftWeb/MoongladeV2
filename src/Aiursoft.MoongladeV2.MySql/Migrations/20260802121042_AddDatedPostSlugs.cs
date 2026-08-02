using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MoongladeV2.MySql.Migrations
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
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostSlugAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DocumentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PublishedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RetiredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
