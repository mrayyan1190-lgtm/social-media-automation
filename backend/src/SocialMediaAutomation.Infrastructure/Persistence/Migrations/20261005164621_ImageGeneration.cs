using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImageGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PostVersions_CreationSource",
                table: "PostVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MediaAssets_SourceType",
                table: "MediaAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiGenerationRequests_Kind",
                table: "AiGenerationRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiCallLogs_Purpose",
                table: "AiCallLogs");

            migrationBuilder.AddColumn<string>(
                name: "ImageApiKeySecretRef",
                table: "PlatformAiConfigurations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImageApiKeyUpdatedAtUtc",
                table: "PlatformAiConfigurations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageBaseUrl",
                table: "PlatformAiConfigurations",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImageEnabled",
                table: "PlatformAiConfigurations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImageModel",
                table: "PlatformAiConfigurations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageProvider",
                table: "PlatformAiConfigurations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageSize",
                table: "PlatformAiConfigurations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenerationModel",
                table: "MediaAssets",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenerationProvider",
                table: "MediaAssets",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GenerationRequestId",
                table: "MediaAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PostVersions_CreationSource",
                table: "PostVersions",
                sql: "[CreationSource] IN ('Manual', 'AiGeneration', 'AiRewrite', 'AiImage')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlatformAiConfigurations_ImageProvider",
                table: "PlatformAiConfigurations",
                sql: "[ImageProvider] IN ('OpenAI', 'Groq', 'OpenAICompatible')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaAssets_SourceType",
                table: "MediaAssets",
                sql: "[SourceType] IN ('Upload', 'AiGenerated')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiGenerationRequests_Kind",
                table: "AiGenerationRequests",
                sql: "[Kind] IN ('Generation', 'Rewrite', 'ImageGeneration')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiCallLogs_Purpose",
                table: "AiCallLogs",
                sql: "[Purpose] IN ('Generation', 'Evaluation', 'Rewrite', 'ModelListing', 'ImageGeneration')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PostVersions_CreationSource",
                table: "PostVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlatformAiConfigurations_ImageProvider",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MediaAssets_SourceType",
                table: "MediaAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiGenerationRequests_Kind",
                table: "AiGenerationRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiCallLogs_Purpose",
                table: "AiCallLogs");

            migrationBuilder.DropColumn(
                name: "ImageApiKeySecretRef",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropColumn(
                name: "ImageApiKeyUpdatedAtUtc",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropColumn(
                name: "ImageBaseUrl",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropColumn(
                name: "ImageEnabled",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropColumn(
                name: "ImageModel",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropColumn(
                name: "ImageProvider",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropColumn(
                name: "ImageSize",
                table: "PlatformAiConfigurations");

            migrationBuilder.DropColumn(
                name: "GenerationModel",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "GenerationProvider",
                table: "MediaAssets");

            migrationBuilder.DropColumn(
                name: "GenerationRequestId",
                table: "MediaAssets");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PostVersions_CreationSource",
                table: "PostVersions",
                sql: "[CreationSource] IN ('Manual', 'AiGeneration', 'AiRewrite')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MediaAssets_SourceType",
                table: "MediaAssets",
                sql: "[SourceType] IN ('Upload')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiGenerationRequests_Kind",
                table: "AiGenerationRequests",
                sql: "[Kind] IN ('Generation', 'Rewrite')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiCallLogs_Purpose",
                table: "AiCallLogs",
                sql: "[Purpose] IN ('Generation', 'Evaluation', 'Rewrite', 'ModelListing')");
        }
    }
}
