using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutOfTheBox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRepositoryCredentialTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RepositoryCredentialHealth",
                columns: table => new
                {
                    RepositoryPath = table.Column<string>(type: "TEXT", nullable: false),
                    LastAuthFailureAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastAuthSuccessAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryCredentialHealth", x => x.RepositoryPath);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepositoryCredentialHealth");
        }
    }
}
