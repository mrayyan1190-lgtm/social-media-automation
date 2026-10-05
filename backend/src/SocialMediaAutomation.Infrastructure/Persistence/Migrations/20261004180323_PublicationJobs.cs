using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicationJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SocialAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExecutionMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PayloadVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PublishAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LocalPublishAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UtcOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    DeliveryState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    NextActionAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DispatchStartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ProviderContainerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalPostId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalPostUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CanceledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CanceledAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationJobs", x => x.Id);
                    table.CheckConstraint("CK_PublicationJobs_Attempts", "[AttemptCount] >= 0 AND [MaxAttempts] >= 1 AND [AttemptCount] <= [MaxAttempts]");
                    table.CheckConstraint("CK_PublicationJobs_DeliveryState", "[DeliveryState] IN ('SCHEDULED', 'PROCESSING', 'WAITING_MEDIA', 'RETRY_PENDING', 'ACTION_REQUIRED', 'RECONCILIATION_REQUIRED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'CANCELED')");
                    table.CheckConstraint("CK_PublicationJobs_ExecutableMode", "[ExecutionMode] <> 'NOT_CONFIGURED'");
                    table.CheckConstraint("CK_PublicationJobs_ExecutionMode", "[ExecutionMode] IN ('LIVE', 'TEST_ACCOUNT', 'SIMULATED', 'NOT_CONFIGURED')");
                    table.CheckConstraint("CK_PublicationJobs_LocalTime", "([LocalPublishAt] IS NULL AND [TimeZoneId] IS NULL AND [UtcOffsetMinutes] IS NULL) OR ([LocalPublishAt] IS NOT NULL AND [TimeZoneId] IS NOT NULL AND [UtcOffsetMinutes] IS NOT NULL)");
                    table.CheckConstraint("CK_PublicationJobs_PayloadJson_Json", "ISJSON([PayloadJson]) = 1");
                    table.CheckConstraint("CK_PublicationJobs_Platform", "[Platform] IN ('LinkedIn', 'Facebook', 'Instagram')");
                    table.CheckConstraint("CK_PublicationJobs_SimulationIsHonest", "NOT ([ExecutionMode] = 'SIMULATED' AND [DeliveryState] = 'PUBLISHED') AND NOT ([ExecutionMode] <> 'SIMULATED' AND [DeliveryState] = 'SIMULATED') AND ([ExecutionMode] <> 'SIMULATED' OR ([ExternalPostId] IS NULL AND [ExternalPostUrl] IS NULL))");
                    table.ForeignKey(
                        name: "FK_PublicationJobs_OrganizationMembers_OrganizationId_CanceledByUserId",
                        columns: x => new { x.OrganizationId, x.CanceledByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublicationJobs_OrganizationMembers_OrganizationId_CreatedByUserId",
                        columns: x => new { x.OrganizationId, x.CreatedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublicationJobs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublicationJobs_ReviewRequests_OrganizationId_PostId_PostVersionId_ReviewRequestId",
                        columns: x => new { x.OrganizationId, x.PostId, x.PostVersionId, x.ReviewRequestId },
                        principalTable: "ReviewRequests",
                        principalColumns: new[] { "OrganizationId", "PostId", "PostVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublicationJobs_SocialAccounts_OrganizationId_SocialAccountId",
                        columns: x => new { x.OrganizationId, x.SocialAccountId },
                        principalTable: "SocialAccounts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_DeliveryState_NextActionAtUtc",
                table: "PublicationJobs",
                columns: new[] { "DeliveryState", "NextActionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_OrganizationId_CanceledByUserId",
                table: "PublicationJobs",
                columns: new[] { "OrganizationId", "CanceledByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_OrganizationId_CreatedByUserId",
                table: "PublicationJobs",
                columns: new[] { "OrganizationId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_OrganizationId_PostId_PostVersionId_ReviewRequestId",
                table: "PublicationJobs",
                columns: new[] { "OrganizationId", "PostId", "PostVersionId", "ReviewRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_OrganizationId_PublishAtUtc",
                table: "PublicationJobs",
                columns: new[] { "OrganizationId", "PublishAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJobs_OrganizationId_SocialAccountId",
                table: "PublicationJobs",
                columns: new[] { "OrganizationId", "SocialAccountId" });

            migrationBuilder.CreateIndex(
                name: "UX_PublicationJobs_LifetimeIntent",
                table: "PublicationJobs",
                columns: new[] { "OrganizationId", "PostVersionId", "SocialAccountId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicationJobs");
        }
    }
}
