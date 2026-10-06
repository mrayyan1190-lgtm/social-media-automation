using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifiedWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignFacts");

            migrationBuilder.DropTable(
                name: "CampaignPlatforms");

            migrationBuilder.DropIndex(
                name: "IX_Posts_OrganizationId_CampaignId_Platform",
                table: "Posts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_CallToAction",
                table: "Campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_ContentLength",
                table: "Campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_EmojiMode",
                table: "Campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_HashtagMode",
                table: "Campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_Objective",
                table: "Campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_ToneOverride",
                table: "Campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiGenerationRequests_RewriteAction",
                table: "AiGenerationRequests");

            migrationBuilder.DropColumn(
                name: "CallToAction",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "CallToActionUrl",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ContentLength",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "EmojiMode",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "HashtagMode",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "Objective",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "TargetAudience",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ToneOverride",
                table: "Campaigns");

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedReviewerUserId",
                table: "ReviewRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CampaignId",
                table: "Posts",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Campaigns",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "Campaigns",
                type: "varchar(7)",
                unicode: false,
                maxLength: 7,
                nullable: false,
                defaultValue: "#2563EB");

            migrationBuilder.AlterColumn<Guid>(
                name: "CampaignId",
                table: "AiGenerationRequests",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_OrganizationId_AssignedReviewerUserId",
                table: "ReviewRequests",
                columns: new[] { "OrganizationId", "AssignedReviewerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_OrganizationId_CampaignId",
                table: "Posts",
                columns: new[] { "OrganizationId", "CampaignId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_Color",
                table: "Campaigns",
                sql: "[Color] LIKE '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiGenerationRequests_RewriteAction",
                table: "AiGenerationRequests",
                sql: "[RewriteAction] IN ('Rewrite', 'Regenerate', 'Shorten', 'Expand', 'MoreProfessional', 'ImproveHook', 'LessPromotional', 'Custom')");

            migrationBuilder.AddForeignKey(
                name: "FK_ReviewRequests_OrganizationMembers_OrganizationId_AssignedReviewerUserId",
                table: "ReviewRequests",
                columns: new[] { "OrganizationId", "AssignedReviewerUserId" },
                principalTable: "OrganizationMembers",
                principalColumns: new[] { "OrganizationId", "UserId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReviewRequests_OrganizationMembers_OrganizationId_AssignedReviewerUserId",
                table: "ReviewRequests");

            migrationBuilder.DropIndex(
                name: "IX_ReviewRequests_OrganizationId_AssignedReviewerUserId",
                table: "ReviewRequests");

            migrationBuilder.DropIndex(
                name: "IX_Posts_OrganizationId_CampaignId",
                table: "Posts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_Color",
                table: "Campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AiGenerationRequests_RewriteAction",
                table: "AiGenerationRequests");

            migrationBuilder.DropColumn(
                name: "AssignedReviewerUserId",
                table: "ReviewRequests");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "Campaigns");

            migrationBuilder.AlterColumn<Guid>(
                name: "CampaignId",
                table: "Posts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Campaigns",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CallToAction",
                table: "Campaigns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CallToActionUrl",
                table: "Campaigns",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentLength",
                table: "Campaigns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmojiMode",
                table: "Campaigns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HashtagMode",
                table: "Campaigns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Objective",
                table: "Campaigns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TargetAudience",
                table: "Campaigns",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ToneOverride",
                table: "Campaigns",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CampaignId",
                table: "AiGenerationRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    SourceNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SuppliedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignFacts_Campaigns_OrganizationId_CampaignId",
                        columns: x => new { x.OrganizationId, x.CampaignId },
                        principalTable: "Campaigns",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignFacts_OrganizationMembers_OrganizationId_SuppliedByUserId",
                        columns: x => new { x.OrganizationId, x.SuppliedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignPlatforms",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignPlatforms", x => new { x.CampaignId, x.Platform });
                    table.CheckConstraint("CK_CampaignPlatforms_Platform", "[Platform] IN ('LinkedIn', 'Facebook', 'Instagram')");
                    table.ForeignKey(
                        name: "FK_CampaignPlatforms_Campaigns_OrganizationId_CampaignId",
                        columns: x => new { x.OrganizationId, x.CampaignId },
                        principalTable: "Campaigns",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_OrganizationId_CampaignId_Platform",
                table: "Posts",
                columns: new[] { "OrganizationId", "CampaignId", "Platform" },
                unique: true,
                filter: "[EditorialState] <> 'ARCHIVED'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_CallToAction",
                table: "Campaigns",
                sql: "[CallToAction] IN ('NONE', 'LEARN_MORE', 'VISIT_WEBSITE', 'BOOK_DEMO', 'SIGN_UP', 'CONTACT_US', 'REGISTER_NOW', 'DOWNLOAD', 'AUTO')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_ContentLength",
                table: "Campaigns",
                sql: "[ContentLength] IN ('SHORT', 'MEDIUM', 'LONG')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_EmojiMode",
                table: "Campaigns",
                sql: "[EmojiMode] IN ('AUTO', 'NONE')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_HashtagMode",
                table: "Campaigns",
                sql: "[HashtagMode] IN ('AUTO', 'NONE')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_Objective",
                table: "Campaigns",
                sql: "[Objective] IN ('BRAND_AWARENESS', 'PRODUCT_LAUNCH', 'LEAD_GENERATION', 'ENGAGEMENT', 'WEBSITE_TRAFFIC', 'ANNOUNCEMENT', 'EDUCATIONAL', 'AUTO')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_ToneOverride",
                table: "Campaigns",
                sql: "[ToneOverride] IN ('Professional', 'Friendly', 'Casual', 'Confident', 'Informative', 'Conversational')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiGenerationRequests_RewriteAction",
                table: "AiGenerationRequests",
                sql: "[RewriteAction] IN ('Shorten', 'Expand', 'MoreProfessional', 'ImproveHook', 'LessPromotional', 'Custom')");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFacts_OrganizationId_CampaignId",
                table: "CampaignFacts",
                columns: new[] { "OrganizationId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFacts_OrganizationId_SuppliedByUserId",
                table: "CampaignFacts",
                columns: new[] { "OrganizationId", "SuppliedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignPlatforms_OrganizationId_CampaignId",
                table: "CampaignPlatforms",
                columns: new[] { "OrganizationId", "CampaignId" });
        }
    }
}
