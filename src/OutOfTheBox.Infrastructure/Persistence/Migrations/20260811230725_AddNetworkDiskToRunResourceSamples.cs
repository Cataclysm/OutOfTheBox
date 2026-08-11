using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutOfTheBox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkDiskToRunResourceSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DiskReadBytesPerSecond",
                table: "RunResourceSamples",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DiskWriteBytesPerSecond",
                table: "RunResourceSamples",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NetworkBytesReceivedPerSecond",
                table: "RunResourceSamples",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NetworkBytesSentPerSecond",
                table: "RunResourceSamples",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiskReadBytesPerSecond",
                table: "RunResourceSamples");

            migrationBuilder.DropColumn(
                name: "DiskWriteBytesPerSecond",
                table: "RunResourceSamples");

            migrationBuilder.DropColumn(
                name: "NetworkBytesReceivedPerSecond",
                table: "RunResourceSamples");

            migrationBuilder.DropColumn(
                name: "NetworkBytesSentPerSecond",
                table: "RunResourceSamples");
        }
    }
}
