using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiImagePosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PostVersionImages",
                columns: table => new
                {
                    Position = table.Column<int>(type: "int", nullable: false),
                    PostVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostVersionImages", x => new { x.PostVersionId, x.Position });
                    table.CheckConstraint("CK_PostVersionImages_Position", "[Position] >= 0 AND [Position] < 4");
                    table.ForeignKey(
                        name: "FK_PostVersionImages_MediaAssets_OrganizationId_MediaAssetId",
                        columns: x => new { x.OrganizationId, x.MediaAssetId },
                        principalTable: "MediaAssets",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostVersionImages_PostVersions_PostVersionId",
                        column: x => x.PostVersionId,
                        principalTable: "PostVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostVersionImages_OrganizationId_MediaAssetId",
                table: "PostVersionImages",
                columns: new[] { "OrganizationId", "MediaAssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostVersionImages_PostVersionId_MediaAssetId",
                table: "PostVersionImages",
                columns: new[] { "PostVersionId", "MediaAssetId" },
                unique: true);
            // Existing single images become position 0, so every version keeps exactly what it showed (and was approved with).
            migrationBuilder.Sql(@"INSERT INTO [PostVersionImages] ([PostVersionId], [Position], [OrganizationId], [MediaAssetId], [AltText])
SELECT [Id], 0, [OrganizationId], [MediaAssetId], [AltText] FROM [PostVersions] WHERE [MediaAssetId] IS NOT NULL");

            // Images are part of the immutable version (AGENTS.md §16, §33): never changed or removed in place.
            // EXEC: CREATE TRIGGER must start its own batch, which the IF-wrapped idempotent deploy script otherwise breaks.
            migrationBuilder.Sql(@"EXEC(N'CREATE TRIGGER [dbo].[TR_PostVersionImages_Immutable] ON [dbo].[PostVersionImages] AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, ''PostVersionImages are immutable. Create a new version instead.'', 1;
END')");

            migrationBuilder.DropForeignKey(
                name: "FK_PostVersions_MediaAssets_OrganizationId_MediaAssetId",
                table: "PostVersions");

            migrationBuilder.DropIndex(
                name: "IX_PostVersions_OrganizationId_MediaAssetId",
                table: "PostVersions");

            migrationBuilder.DropColumn(
                name: "AltText",
                table: "PostVersions");

            migrationBuilder.DropColumn(
                name: "MediaAssetId",
                table: "PostVersions");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AltText",
                table: "PostVersions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaAssetId",
                table: "PostVersions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostVersions_OrganizationId_MediaAssetId",
                table: "PostVersions",
                columns: new[] { "OrganizationId", "MediaAssetId" });

            migrationBuilder.AddForeignKey(
                name: "FK_PostVersions_MediaAssets_OrganizationId_MediaAssetId",
                table: "PostVersions",
                columns: new[] { "OrganizationId", "MediaAssetId" },
                principalTable: "MediaAssets",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Back to one image per version: the first image survives; images 2-4 cannot be represented and are lost.
            migrationBuilder.Sql("DISABLE TRIGGER [dbo].[TR_PostVersions_Immutable] ON [dbo].[PostVersions]");
            migrationBuilder.Sql(@"UPDATE v SET [MediaAssetId] = i.[MediaAssetId], [AltText] = i.[AltText]
FROM [PostVersions] v JOIN [PostVersionImages] i ON i.[PostVersionId] = v.[Id] AND i.[Position] = 0");
            migrationBuilder.Sql("ENABLE TRIGGER [dbo].[TR_PostVersions_Immutable] ON [dbo].[PostVersions]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_PostVersionImages_Immutable]");

            migrationBuilder.DropTable(
                name: "PostVersionImages");

        }
    }
}
