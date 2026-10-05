using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiGenerationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Platforms = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RewriteAction = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CustomInstruction = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    InputSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeasedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGenerationRequests", x => x.Id);
                    table.UniqueConstraint("AK_AiGenerationRequests_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_AiGenerationRequests_InputSnapshot_Json", "ISJSON([InputSnapshot]) = 1");
                    table.CheckConstraint("CK_AiGenerationRequests_Kind", "[Kind] IN ('Generation', 'Rewrite')");
                    table.CheckConstraint("CK_AiGenerationRequests_Platforms_Json", "ISJSON([Platforms]) = 1");
                    table.CheckConstraint("CK_AiGenerationRequests_Provider", "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible')");
                    table.CheckConstraint("CK_AiGenerationRequests_RewriteAction", "[RewriteAction] IN ('Shorten', 'Expand', 'MoreProfessional', 'ImproveHook', 'LessPromotional', 'Custom')");
                    table.CheckConstraint("CK_AiGenerationRequests_Status", "[Status] IN ('PENDING', 'PROCESSING', 'COMPLETED', 'FAILED')");
                    table.ForeignKey(
                        name: "FK_AiGenerationRequests_Campaigns_OrganizationId_CampaignId",
                        columns: x => new { x.OrganizationId, x.CampaignId },
                        principalTable: "Campaigns",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiGenerationRequests_OrganizationMembers_OrganizationId_RequestedByUserId",
                        columns: x => new { x.OrganizationId, x.RequestedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiGenerationRequests_Posts_OrganizationId_PostId",
                        columns: x => new { x.OrganizationId, x.PostId },
                        principalTable: "Posts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiProviderSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProtectedApiKey = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ApiKeyHint = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderSettings", x => x.Id);
                    table.CheckConstraint("CK_AiProviderSettings_Provider", "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible')");
                    table.ForeignKey(
                        name: "FK_AiProviderSettings_OrganizationMembers_OrganizationId_UpdatedByUserId",
                        columns: x => new { x.OrganizationId, x.UpdatedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiProviderSettings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiCallLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HttpStatus = table.Column<int>(type: "int", nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Diagnostic = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCallLogs", x => x.Id);
                    table.CheckConstraint("CK_AiCallLogs_Provider", "[Provider] IN ('OpenAI', 'Groq', 'OpenAICompatible')");
                    table.CheckConstraint("CK_AiCallLogs_Purpose", "[Purpose] IN ('Generation', 'Evaluation', 'Rewrite', 'ModelListing')");
                    table.ForeignKey(
                        name: "FK_AiCallLogs_AiGenerationRequests_OrganizationId_RequestId",
                        columns: x => new { x.OrganizationId, x.RequestId },
                        principalTable: "AiGenerationRequests",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiRequestOutputs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutputJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiRequestOutputs", x => x.Id);
                    table.CheckConstraint("CK_AiRequestOutputs_OutputJson_Json", "ISJSON([OutputJson]) = 1");
                    table.CheckConstraint("CK_AiRequestOutputs_Platform", "[Platform] IN ('LinkedIn', 'Facebook', 'Instagram')");
                    table.ForeignKey(
                        name: "FK_AiRequestOutputs_AiGenerationRequests_OrganizationId_RequestId",
                        columns: x => new { x.OrganizationId, x.RequestId },
                        principalTable: "AiGenerationRequests",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiRequestOutputs_PostVersions_OrganizationId_PostId_PostVersionId",
                        columns: x => new { x.OrganizationId, x.PostId, x.PostVersionId },
                        principalTable: "PostVersions",
                        principalColumns: new[] { "OrganizationId", "PostId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContentEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BrandAlignment = table.Column<int>(type: "int", nullable: true),
                    AudienceRelevance = table.Column<int>(type: "int", nullable: true),
                    Clarity = table.Column<int>(type: "int", nullable: true),
                    PlatformFit = table.Column<int>(type: "int", nullable: true),
                    CtaRelevance = table.Column<int>(type: "int", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Issues = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HasCriticalIssue = table.Column<bool>(type: "bit", nullable: false),
                    EvaluatorModel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PromptVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentEvaluations", x => x.Id);
                    table.CheckConstraint("CK_ContentEvaluations_Issues_Json", "ISJSON([Issues]) = 1");
                    table.ForeignKey(
                        name: "FK_ContentEvaluations_AiGenerationRequests_OrganizationId_RequestId",
                        columns: x => new { x.OrganizationId, x.RequestId },
                        principalTable: "AiGenerationRequests",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContentEvaluations_PostVersions_OrganizationId_PostId_PostVersionId",
                        columns: x => new { x.OrganizationId, x.PostId, x.PostVersionId },
                        principalTable: "PostVersions",
                        principalColumns: new[] { "OrganizationId", "PostId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCallLogs_OrganizationId_RequestId",
                table: "AiCallLogs",
                columns: new[] { "OrganizationId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationRequests_OrganizationId_CampaignId_CreatedAtUtc",
                table: "AiGenerationRequests",
                columns: new[] { "OrganizationId", "CampaignId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationRequests_OrganizationId_PostId",
                table: "AiGenerationRequests",
                columns: new[] { "OrganizationId", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationRequests_OrganizationId_RequestedByUserId",
                table: "AiGenerationRequests",
                columns: new[] { "OrganizationId", "RequestedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationRequests_Status_NextAttemptAtUtc",
                table: "AiGenerationRequests",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderSettings_OrganizationId",
                table: "AiProviderSettings",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderSettings_OrganizationId_UpdatedByUserId",
                table: "AiProviderSettings",
                columns: new[] { "OrganizationId", "UpdatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiRequestOutputs_OrganizationId_PostId_PostVersionId",
                table: "AiRequestOutputs",
                columns: new[] { "OrganizationId", "PostId", "PostVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiRequestOutputs_OrganizationId_RequestId",
                table: "AiRequestOutputs",
                columns: new[] { "OrganizationId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiRequestOutputs_RequestId_Platform",
                table: "AiRequestOutputs",
                columns: new[] { "RequestId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentEvaluations_OrganizationId_PostId_PostVersionId",
                table: "ContentEvaluations",
                columns: new[] { "OrganizationId", "PostId", "PostVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentEvaluations_OrganizationId_RequestId",
                table: "ContentEvaluations",
                columns: new[] { "OrganizationId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentEvaluations_PostVersionId",
                table: "ContentEvaluations",
                column: "PostVersionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCallLogs");

            migrationBuilder.DropTable(
                name: "AiProviderSettings");

            migrationBuilder.DropTable(
                name: "AiRequestOutputs");

            migrationBuilder.DropTable(
                name: "ContentEvaluations");

            migrationBuilder.DropTable(
                name: "AiGenerationRequests");
        }
    }
}
