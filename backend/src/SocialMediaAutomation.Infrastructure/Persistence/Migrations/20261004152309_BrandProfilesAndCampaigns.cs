using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BrandProfilesAndCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationMembers_OrganizationId_UserId",
                table: "OrganizationMembers");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrganizationMembers_OrganizationId_UserId",
                table: "OrganizationMembers",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateTable(
                name: "BrandProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BrandName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Industry = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BusinessDescription = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ProductsServices = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ValueProposition = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DefaultAudience = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    VoiceTraits = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultLanguage = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    PreferredCtaStyle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PreferredTerms = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProhibitedTerms = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BrandHashtags = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmojiPreference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WebsiteUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    MarketNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandProfiles", x => x.Id);
                    table.CheckConstraint("CK_BrandProfiles_BrandHashtags_Json", "ISJSON([BrandHashtags]) = 1");
                    table.CheckConstraint("CK_BrandProfiles_EmojiPreference", "[EmojiPreference] IN ('None', 'Minimal', 'Moderate', 'Expressive')");
                    table.CheckConstraint("CK_BrandProfiles_PreferredTerms_Json", "ISJSON([PreferredTerms]) = 1");
                    table.CheckConstraint("CK_BrandProfiles_ProhibitedTerms_Json", "ISJSON([ProhibitedTerms]) = 1");
                    table.CheckConstraint("CK_BrandProfiles_VoiceTraits_Json", "ISJSON([VoiceTraits]) = 1");
                    table.ForeignKey(
                        name: "FK_BrandProfiles_OrganizationMembers_OrganizationId_UpdatedByUserId",
                        columns: x => new { x.OrganizationId, x.UpdatedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrandProfiles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Objective = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    KeyMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TargetAudience = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PainPoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrimaryBenefit = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CallToAction = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DestinationUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ContentCategory = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ToneOverride = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LengthPreference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    HashtagPreference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EmojiPreference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ContentAngle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HookDirection = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EmbargoNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Platforms = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.UniqueConstraint("AK_Campaigns_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Campaigns_EmojiPreference", "[EmojiPreference] IN ('None', 'Minimal', 'Moderate', 'Expressive')");
                    table.CheckConstraint("CK_Campaigns_HashtagPreference", "[HashtagPreference] IN ('Default', 'None', 'Few', 'Many')");
                    table.CheckConstraint("CK_Campaigns_LengthPreference", "[LengthPreference] IN ('Default', 'Short', 'Medium', 'Long')");
                    table.CheckConstraint("CK_Campaigns_Platforms_Json", "ISJSON([Platforms]) = 1");
                    table.CheckConstraint("CK_Campaigns_Status", "[Status] IN ('Active', 'Archived')");
                    table.ForeignKey(
                        name: "FK_Campaigns_OrganizationMembers_OrganizationId_CreatedByUserId",
                        columns: x => new { x.OrganizationId, x.CreatedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Campaigns_OrganizationMembers_OrganizationId_UpdatedByUserId",
                        columns: x => new { x.OrganizationId, x.UpdatedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Campaigns_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CampaignFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SourceNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SuppliedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_BrandProfiles_OrganizationId",
                table: "BrandProfiles",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrandProfiles_OrganizationId_UpdatedByUserId",
                table: "BrandProfiles",
                columns: new[] { "OrganizationId", "UpdatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFacts_OrganizationId_CampaignId",
                table: "CampaignFacts",
                columns: new[] { "OrganizationId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignFacts_OrganizationId_SuppliedByUserId",
                table: "CampaignFacts",
                columns: new[] { "OrganizationId", "SuppliedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_OrganizationId_CreatedByUserId",
                table: "Campaigns",
                columns: new[] { "OrganizationId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_OrganizationId_UpdatedAtUtc",
                table: "Campaigns",
                columns: new[] { "OrganizationId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_OrganizationId_UpdatedByUserId",
                table: "Campaigns",
                columns: new[] { "OrganizationId", "UpdatedByUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandProfiles");

            migrationBuilder.DropTable(
                name: "CampaignFacts");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrganizationMembers_OrganizationId_UserId",
                table: "OrganizationMembers");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_OrganizationId_UserId",
                table: "OrganizationMembers",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);
        }
    }
}
