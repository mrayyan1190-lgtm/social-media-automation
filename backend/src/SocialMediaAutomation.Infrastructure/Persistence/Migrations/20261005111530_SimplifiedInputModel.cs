using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Simplified onboarding/campaign input model: industry moves to the workspace, the brand profile keeps four inputs
    /// (plus optional prohibited terms), campaigns use enum objective/CTA/options and a normalized CampaignPlatforms table.
    /// Existing rows are converted; free text that has no new field is folded into the description.
    /// Data statements run through EXEC so the idempotent deploy script still compiles before the new columns exist.
    /// </summary>
    public partial class SimplifiedInputModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Organizations: Industry (from the brand profile) and UpdatedAtUtc -----------------------------------
            migrationBuilder.AddColumn<string>(
                name: "Industry", table: "Organizations", type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc", table: "Organizations", type: "datetime2(3)", nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
            migrationBuilder.Sql(@"EXEC(N'UPDATE o SET o.Industry = COALESCE(NULLIF(LEFT(b.Industry, 200), N''''), N''Unspecified''), o.UpdatedAtUtc = o.CreatedAtUtc
FROM dbo.Organizations o LEFT JOIN dbo.BrandProfiles b ON b.OrganizationId = o.Id')");

            // ---- BrandProfiles: description, target audience, tone, website, prohibited terms ---------------------------
            migrationBuilder.RenameColumn(name: "DefaultAudience", table: "BrandProfiles", newName: "TargetAudience");
            migrationBuilder.RenameColumn(name: "WebsiteUrl", table: "BrandProfiles", newName: "Website");
            migrationBuilder.AddColumn<string>(
                name: "BrandTone", table: "BrandProfiles", type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Professional");
            migrationBuilder.Sql(@"EXEC(N'UPDATE dbo.BrandProfiles SET BusinessDescription = LEFT(CONCAT(BusinessDescription,
    N'' Products/services: '', ProductsServices, N'' Value proposition: '', ValueProposition), 2000)')");
            foreach (var check in new[] { "BrandHashtags_Json", "EmojiPreference", "PreferredTerms_Json", "VoiceTraits_Json" })
                migrationBuilder.DropCheckConstraint(name: $"CK_BrandProfiles_{check}", table: "BrandProfiles");
            foreach (var column in new[]
                     {
                         "BrandName", "Industry", "ProductsServices", "ValueProposition", "VoiceTraits", "DefaultLanguage",
                         "PreferredCtaStyle", "PreferredTerms", "BrandHashtags", "EmojiPreference", "MarketNotes",
                     })
                migrationBuilder.DropColumn(name: column, table: "BrandProfiles");
            migrationBuilder.AddCheckConstraint(
                name: "CK_BrandProfiles_BrandTone", table: "BrandProfiles",
                sql: "[BrandTone] IN ('Professional', 'Friendly', 'Casual', 'Confident', 'Informative', 'Conversational')");

            // ---- CampaignPlatforms: one row per selected platform, same-tenant FK to the campaign ----------------------
            migrationBuilder.CreateTable(
                name: "CampaignPlatforms",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                name: "IX_CampaignPlatforms_OrganizationId_CampaignId", table: "CampaignPlatforms", columns: new[] { "OrganizationId", "CampaignId" });
            migrationBuilder.Sql(@"EXEC(N'INSERT INTO dbo.CampaignPlatforms (CampaignId, Platform, OrganizationId)
SELECT DISTINCT c.Id, j.[value], c.OrganizationId FROM dbo.Campaigns c CROSS APPLY OPENJSON(c.Platforms) j')");

            // ---- Campaigns ---------------------------------------------------------------------------------------------
            foreach (var check in new[] { "EmojiPreference", "HashtagPreference", "LengthPreference", "Platforms_Json", "Status" })
                migrationBuilder.DropCheckConstraint(name: $"CK_Campaigns_{check}", table: "Campaigns");
            migrationBuilder.RenameColumn(name: "KeyMessage", table: "Campaigns", newName: "Description");
            migrationBuilder.RenameColumn(name: "DestinationUrl", table: "Campaigns", newName: "CallToActionUrl");
            migrationBuilder.AddColumn<string>(
                name: "ContentLength", table: "Campaigns", type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "MEDIUM");
            migrationBuilder.AddColumn<string>(
                name: "HashtagMode", table: "Campaigns", type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "AUTO");
            migrationBuilder.AddColumn<string>(
                name: "EmojiMode", table: "Campaigns", type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "AUTO");
            // Free-text objectives have no enum equivalent: keep the wording in the description and start from BRAND_AWARENESS.
            migrationBuilder.Sql(@"EXEC(N'UPDATE c SET
    c.Description = LEFT(CONCAT(c.Topic, N'': '', c.Description, N'' (Objective: '', c.Objective, N'')''), 2000),
    c.Objective = N''BRAND_AWARENESS'',
    c.TargetAudience = COALESCE(c.TargetAudience, b.TargetAudience, N''''),
    c.CallToAction = CASE WHEN c.CallToActionUrl LIKE N''https://%'' THEN N''LEARN_MORE'' ELSE N''NONE'' END,
    c.CallToActionUrl = CASE WHEN c.CallToActionUrl LIKE N''https://%'' THEN c.CallToActionUrl END,
    c.ToneOverride = NULL,
    c.ContentLength = CASE c.LengthPreference WHEN N''Short'' THEN N''SHORT'' WHEN N''Long'' THEN N''LONG'' ELSE N''MEDIUM'' END,
    c.HashtagMode = CASE c.HashtagPreference WHEN N''None'' THEN N''NONE'' ELSE N''AUTO'' END,
    c.EmojiMode = CASE c.EmojiPreference WHEN N''None'' THEN N''NONE'' ELSE N''AUTO'' END
FROM dbo.Campaigns c LEFT JOIN dbo.BrandProfiles b ON b.OrganizationId = c.OrganizationId')");
            foreach (var column in new[]
                     {
                         "Topic", "PainPoint", "PrimaryBenefit", "ContentCategory", "ContentAngle", "HookDirection", "StartDate", "EndDate",
                         "EmbargoNotes", "LengthPreference", "HashtagPreference", "EmojiPreference", "Platforms",
                     })
                migrationBuilder.DropColumn(name: column, table: "Campaigns");
            migrationBuilder.AlterColumn<string>(
                name: "Objective", table: "Campaigns", type: "nvarchar(32)", maxLength: 32, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(500)", oldMaxLength: 500);
            migrationBuilder.AlterColumn<string>(
                name: "TargetAudience", table: "Campaigns", type: "nvarchar(2000)", maxLength: 2000, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(2000)", oldMaxLength: 2000, oldNullable: true);
            migrationBuilder.Sql(@"EXEC(N'UPDATE dbo.Campaigns SET CallToAction = N''NONE'' WHERE CallToAction IS NULL')");
            migrationBuilder.AlterColumn<string>(
                name: "CallToAction", table: "Campaigns", type: "nvarchar(32)", maxLength: 32, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(200)", oldMaxLength: 200, oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "ToneOverride", table: "Campaigns", type: "nvarchar(32)", maxLength: 32, nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(200)", oldMaxLength: 200, oldNullable: true);
            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_Objective", table: "Campaigns",
                sql: "[Objective] IN ('BRAND_AWARENESS', 'PRODUCT_LAUNCH', 'LEAD_GENERATION', 'ENGAGEMENT', 'WEBSITE_TRAFFIC', 'ANNOUNCEMENT', 'EDUCATIONAL')");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_CallToAction", table: "Campaigns",
                sql: "[CallToAction] IN ('NONE', 'LEARN_MORE', 'VISIT_WEBSITE', 'BOOK_DEMO', 'SIGN_UP', 'CONTACT_US', 'REGISTER_NOW', 'DOWNLOAD')");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_ToneOverride", table: "Campaigns",
                sql: "[ToneOverride] IN ('Professional', 'Friendly', 'Casual', 'Confident', 'Informative', 'Conversational')");
            migrationBuilder.AddCheckConstraint(name: "CK_Campaigns_ContentLength", table: "Campaigns", sql: "[ContentLength] IN ('SHORT', 'MEDIUM', 'LONG')");
            migrationBuilder.AddCheckConstraint(name: "CK_Campaigns_HashtagMode", table: "Campaigns", sql: "[HashtagMode] IN ('AUTO', 'NONE')");
            migrationBuilder.AddCheckConstraint(name: "CK_Campaigns_EmojiMode", table: "Campaigns", sql: "[EmojiMode] IN ('AUTO', 'NONE')");
            migrationBuilder.AddCheckConstraint(name: "CK_Campaigns_Status", table: "Campaigns", sql: "[Status] IN ('Draft', 'Active', 'Archived')");

            // ---- CampaignFacts: source note becomes optional -------------------------------------------------------------
            migrationBuilder.AlterColumn<string>(
                name: "SourceNote", table: "CampaignFacts", type: "nvarchar(500)", maxLength: 500, nullable: true,
                oldClrType: typeof(string), oldType: "nvarchar(500)", oldMaxLength: 500);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            // The old model held free-text fields this one does not keep; restore from a backup instead of guessing them.
            throw new NotSupportedException("SimplifiedInputModel cannot be reverted automatically; restore the database from a backup.");
    }
}
