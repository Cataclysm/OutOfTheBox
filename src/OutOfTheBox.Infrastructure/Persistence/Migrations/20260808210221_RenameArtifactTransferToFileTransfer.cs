using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutOfTheBox.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Renames the <c>ArtifactTransfer</c> run kind to <c>FileTransfer</c>, and its two
    /// kind-specific columns to match. The scaffolding tool only caught the column renames on its
    /// own - <c>Kind</c> is mapped via <c>HasConversion&lt;string&gt;()</c>
    /// (<see cref="OutOfTheBoxDbContext"/>), so existing rows have the literal text
    /// <c>"ArtifactTransfer"</c> persisted, not an int; without the explicit <c>UPDATE</c> below,
    /// any pre-existing file-transfer row would fail to read back at all once the enum member was
    /// renamed in code (<see cref="Domain.Runs.RunKind"/> no longer has an <c>ArtifactTransfer</c>
    /// member to parse that string into).
    /// </summary>
    /// <inheritdoc />
    public partial class RenameArtifactTransferToFileTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ArtifactSizeBytes",
                table: "Runs",
                newName: "FileSizeBytes");

            migrationBuilder.RenameColumn(
                name: "ArtifactPath",
                table: "Runs",
                newName: "FilePath");

            migrationBuilder.Sql("UPDATE Runs SET Kind = 'FileTransfer' WHERE Kind = 'ArtifactTransfer';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Runs SET Kind = 'ArtifactTransfer' WHERE Kind = 'FileTransfer';");

            migrationBuilder.RenameColumn(
                name: "FileSizeBytes",
                table: "Runs",
                newName: "ArtifactSizeBytes");

            migrationBuilder.RenameColumn(
                name: "FilePath",
                table: "Runs",
                newName: "ArtifactPath");
        }
    }
}
