using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorPal.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddClipDurationMs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClipDurationMs",
                table: "NoiseEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClipDurationMs",
                table: "NoiseEvents");
        }
    }
}
