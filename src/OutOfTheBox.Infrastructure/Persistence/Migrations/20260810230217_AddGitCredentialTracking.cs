using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutOfTheBox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitCredentialTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GitHostAuthorizations",
                columns: table => new
                {
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHostAuthorizations", x => x.Host);
                });

            migrationBuilder.CreateTable(
                name: "GitHostCredentialHealth",
                columns: table => new
                {
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    LastAuthFailureAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastAuthSuccessAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHostCredentialHealth", x => x.Host);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHostAuthorizations");

            migrationBuilder.DropTable(
                name: "GitHostCredentialHealth");
        }
    }
}
