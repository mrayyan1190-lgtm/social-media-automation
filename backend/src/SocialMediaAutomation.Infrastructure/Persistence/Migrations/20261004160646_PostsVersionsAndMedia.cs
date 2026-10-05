using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PostsVersionsAndMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ObjectKey = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaAssets", x => x.Id);
                    table.UniqueConstraint("AK_MediaAssets_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_MediaAssets_SourceType", "[SourceType] IN ('Upload')");
                    table.ForeignKey(
                        name: "FK_MediaAssets_OrganizationMembers_OrganizationId_CreatedByUserId",
                        columns: x => new { x.OrganizationId, x.CreatedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MediaAssets_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EditorialState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActiveApprovalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.Id);
                    table.UniqueConstraint("AK_Posts_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Posts_EditorialState", "[EditorialState] IN ('DRAFT', 'IN_REVIEW', 'CHANGES_REQUESTED', 'APPROVED', 'ARCHIVED')");
                    table.CheckConstraint("CK_Posts_Platform", "[Platform] IN ('LinkedIn', 'Facebook', 'Instagram')");
                    table.ForeignKey(
                        name: "FK_Posts_Campaigns_OrganizationId_CampaignId",
                        columns: x => new { x.OrganizationId, x.CampaignId },
                        principalTable: "Campaigns",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Posts_OrganizationMembers_OrganizationId_CreatedByUserId",
                        columns: x => new { x.OrganizationId, x.CreatedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PostVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Hook = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                    CallToAction = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DestinationUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Hashtags = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SocialAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AltText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreationSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostVersions", x => x.Id);
                    table.UniqueConstraint("AK_PostVersions_OrganizationId_PostId_Id", x => new { x.OrganizationId, x.PostId, x.Id });
                    table.CheckConstraint("CK_PostVersions_CreationSource", "[CreationSource] IN ('Manual', 'AiGeneration', 'AiRewrite')");
                    table.CheckConstraint("CK_PostVersions_Hashtags_Json", "ISJSON([Hashtags]) = 1");
                    table.ForeignKey(
                        name: "FK_PostVersions_MediaAssets_OrganizationId_MediaAssetId",
                        columns: x => new { x.OrganizationId, x.MediaAssetId },
                        principalTable: "MediaAssets",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostVersions_OrganizationMembers_OrganizationId_CreatedByUserId",
                        columns: x => new { x.OrganizationId, x.CreatedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostVersions_Posts_OrganizationId_PostId",
                        columns: x => new { x.OrganizationId, x.PostId },
                        principalTable: "Posts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_ObjectKey",
                table: "MediaAssets",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_OrganizationId_CreatedByUserId",
                table: "MediaAssets",
                columns: new[] { "OrganizationId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CurrentVersionId",
                table: "Posts",
                column: "CurrentVersionId",
                unique: true,
                filter: "[CurrentVersionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_OrganizationId_CampaignId_Platform",
                table: "Posts",
                columns: new[] { "OrganizationId", "CampaignId", "Platform" },
                unique: true,
                filter: "[EditorialState] <> 'ARCHIVED'");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_OrganizationId_CreatedByUserId",
                table: "Posts",
                columns: new[] { "OrganizationId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_OrganizationId_Id_CurrentVersionId",
                table: "Posts",
                columns: new[] { "OrganizationId", "Id", "CurrentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostVersions_OrganizationId_CreatedByUserId",
                table: "PostVersions",
                columns: new[] { "OrganizationId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostVersions_OrganizationId_MediaAssetId",
                table: "PostVersions",
                columns: new[] { "OrganizationId", "MediaAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostVersions_PostId_VersionNumber",
                table: "PostVersions",
                columns: new[] { "PostId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_PostVersions_OrganizationId_Id_CurrentVersionId",
                table: "Posts",
                columns: new[] { "OrganizationId", "Id", "CurrentVersionId" },
                principalTable: "PostVersions",
                principalColumns: new[] { "OrganizationId", "PostId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Append-only guarantees (AGENTS.md §16, §32): reviewed content and stored media identities are never changed in place.
            // EXEC: CREATE TRIGGER must start its own batch, which the IF-wrapped idempotent deploy script otherwise breaks.
            migrationBuilder.Sql(@"EXEC(N'CREATE TRIGGER [dbo].[TR_PostVersions_Immutable] ON [dbo].[PostVersions] AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, ''PostVersions are immutable. Create a new version instead.'', 1;
END')");
            migrationBuilder.Sql(@"EXEC(N'CREATE TRIGGER [dbo].[TR_MediaAssets_Immutable] ON [dbo].[MediaAssets] AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, ''MediaAssets are immutable. Upload a new asset instead.'', 1;
END')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_PostVersions_Immutable]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_MediaAssets_Immutable]");
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_PostVersions_OrganizationId_Id_CurrentVersionId",
                table: "Posts");

            migrationBuilder.DropTable(
                name: "PostVersions");

            migrationBuilder.DropTable(
                name: "MediaAssets");

            migrationBuilder.DropTable(
                name: "Posts");
        }
    }
}
