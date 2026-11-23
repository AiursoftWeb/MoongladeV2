using System.Diagnostics.CodeAnalysis;
﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MoongladeV2.Sqlite.Migrations
{
    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public partial class AddGuestCommentEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GuestEmail",
                table: "Comments",
                type: "TEXT",
                maxLength: 254,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuestEmail",
                table: "Comments");
        }
    }
}
