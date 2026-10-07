using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VisionApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VisionApiKeySecretRef",
                table: "PlatformAiConfigurations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VisionApiKeyUpdatedAtUtc",
                table: "PlatformAiConfigurations",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisionApiKeySecretRef",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropColumn(
                name: "VisionApiKeyUpdatedAtUtc",
                table: "PlatformAiConfigurations");
        }
    }
}
