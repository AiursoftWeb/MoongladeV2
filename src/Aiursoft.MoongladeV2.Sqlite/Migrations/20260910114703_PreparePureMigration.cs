using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MoongladeV2.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class PreparePureMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "Comments",
                type: "TEXT",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 450);

            migrationBuilder.AddColumn<string>(
                name: "GuestName",
                table: "Comments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MetaDescription = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    HtmlContent = table.Column<string>(type: "TEXT", maxLength: 65535, nullable: false),
                    CssContent = table.Column<string>(type: "TEXT", maxLength: 65535, nullable: false),
                    HideSidebar = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomPages", x => x.Id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Comments_Author",
                table: "Comments",
                sql: "(`UserId` IS NOT NULL AND `GuestName` IS NULL) OR (`UserId` IS NULL AND `GuestName` IS NOT NULL AND `GuestName` <> '')");

            migrationBuilder.CreateIndex(
                name: "IX_CustomPages_Slug",
                table: "CustomPages",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomPages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Comments_Author",
                table: "Comments");

            migrationBuilder.Sql("DELETE FROM \"Comments\" WHERE \"UserId\" IS NULL;");

            migrationBuilder.DropColumn(
                name: "GuestName",
                table: "Comments");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                table: "Comments",
                type: "TEXT",
                maxLength: 450,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 450,
                oldNullable: true);
        }
    }
}
