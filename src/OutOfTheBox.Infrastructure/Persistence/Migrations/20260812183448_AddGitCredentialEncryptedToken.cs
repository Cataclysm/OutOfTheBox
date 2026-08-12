using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutOfTheBox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitCredentialEncryptedToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "EncryptedToken",
                table: "GitHostAuthorizations",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedToken",
                table: "GitHostAuthorizations");
        }
    }
}
