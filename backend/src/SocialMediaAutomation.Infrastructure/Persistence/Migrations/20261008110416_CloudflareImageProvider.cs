using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CloudflareImageProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PlatformAiConfigurations_ImageProvider",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlatformAiConfigurations_Provider",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiProviderSettings_Provider",
                table: "AiProviderSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiGenerationRequests_Provider",
                table: "AiGenerationRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiCallLogs_Provider",
                table: "AiCallLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlatformAiConfigurations_ImageProvider",
                table: "PlatformAiConfigurations",
                sql: "[ImageProvider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI', 'CloudflareWorkersAI')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlatformAiConfigurations_Provider",
                table: "PlatformAiConfigurations",
                sql: "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI', 'CloudflareWorkersAI')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiProviderSettings_Provider",
                table: "AiProviderSettings",
                sql: "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI', 'CloudflareWorkersAI')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiGenerationRequests_Provider",
                table: "AiGenerationRequests",
                sql: "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI', 'CloudflareWorkersAI')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiCallLogs_Provider",
                table: "AiCallLogs",
                sql: "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI', 'CloudflareWorkersAI')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PlatformAiConfigurations_ImageProvider",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlatformAiConfigurations_Provider",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiProviderSettings_Provider",
                table: "AiProviderSettings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiGenerationRequests_Provider",
                table: "AiGenerationRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiCallLogs_Provider",
                table: "AiCallLogs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlatformAiConfigurations_ImageProvider",
                table: "PlatformAiConfigurations",
                sql: "[ImageProvider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlatformAiConfigurations_Provider",
                table: "PlatformAiConfigurations",
                sql: "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiProviderSettings_Provider",
                table: "AiProviderSettings",
                sql: "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiGenerationRequests_Provider",
                table: "AiGenerationRequests",
                sql: "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiCallLogs_Provider",
                table: "AiCallLogs",
                sql: "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible', 'HuggingFace', 'StabilityAI')");
        }
    }
}
