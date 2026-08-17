using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutOfTheBox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMcpToolPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpToolPermissions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpToolPermissions", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpToolPermissions");
        }
    }
}
