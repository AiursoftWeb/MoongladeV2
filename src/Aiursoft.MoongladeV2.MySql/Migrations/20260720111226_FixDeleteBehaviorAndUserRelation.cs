using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MoongladeV2.MySql.Migrations
{
    /// <inheritdoc />
    public partial class FixDeleteBehaviorAndUserRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Comments_AspNetUsers_UserId",
                table: "Comments");

            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId",
                table: "MarkdownDocuments");

            migrationBuilder.AddColumn<string>(
                name: "UserId1",
                table: "MarkdownDocuments",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_UserId1",
                table: "MarkdownDocuments",
                column: "UserId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_AspNetUsers_UserId",
                table: "Comments",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId",
                table: "MarkdownDocuments",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId1",
                table: "MarkdownDocuments",
                column: "UserId1",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Comments_AspNetUsers_UserId",
                table: "Comments");

            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId",
                table: "MarkdownDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId1",
                table: "MarkdownDocuments");

            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_UserId1",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "MarkdownDocuments");

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_AspNetUsers_UserId",
                table: "Comments",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId",
                table: "MarkdownDocuments",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
