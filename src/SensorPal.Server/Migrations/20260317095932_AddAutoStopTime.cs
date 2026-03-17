using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SensorPal.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoStopTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoStopTime",
                table: "AppSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoStopTime",
                table: "AppSettings");
        }
    }
}
