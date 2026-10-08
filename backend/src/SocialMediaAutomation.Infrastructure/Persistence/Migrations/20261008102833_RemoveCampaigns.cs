using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiGenerationRequests_Campaigns_OrganizationId_CampaignId",
                table: "AiGenerationRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Posts_Campaigns_OrganizationId_CampaignId",
                table: "Posts");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Posts_OrganizationId_CampaignId",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_AiGenerationRequests_OrganizationId_CampaignId_CreatedAtUtc",
                table: "AiGenerationRequests");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "AiGenerationRequests");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationRequests_OrganizationId_CreatedAtUtc",
                table: "AiGenerationRequests",
                columns: new[] { "OrganizationId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiGenerationRequests_OrganizationId_CreatedAtUtc",
                table: "AiGenerationRequests");

            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "Posts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "AiGenerationRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Color = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false, defaultValue: "#2563EB"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.UniqueConstraint("AK_Campaigns_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Campaigns_Color", "[Color] LIKE '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'");
                    table.CheckConstraint("CK_Campaigns_Status", "[Status] IN ('Draft', 'Active', 'Archived')");
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

            migrationBuilder.CreateIndex(
                name: "IX_Posts_OrganizationId_CampaignId",
                table: "Posts",
                columns: new[] { "OrganizationId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationRequests_OrganizationId_CampaignId_CreatedAtUtc",
                table: "AiGenerationRequests",
                columns: new[] { "OrganizationId", "CampaignId", "CreatedAtUtc" });

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

            migrationBuilder.AddForeignKey(
                name: "FK_AiGenerationRequests_Campaigns_OrganizationId_CampaignId",
                table: "AiGenerationRequests",
                columns: new[] { "OrganizationId", "CampaignId" },
                principalTable: "Campaigns",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_Campaigns_OrganizationId_CampaignId",
                table: "Posts",
                columns: new[] { "OrganizationId", "CampaignId" },
                principalTable: "Campaigns",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
