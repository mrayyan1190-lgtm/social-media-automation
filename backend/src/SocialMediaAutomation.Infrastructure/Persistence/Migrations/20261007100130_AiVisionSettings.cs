using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiVisionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VisionEnabled",
                table: "PlatformAiConfigurations",
                type: "bit",
                nullable: false,
                // Existing installations keep image understanding on (it still needs a saved Hugging Face token).
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "VisionModel",
                table: "PlatformAiConfigurations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisionEnabled",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropColumn(
                name: "VisionModel",
                table: "PlatformAiConfigurations");
        }
    }
}
