using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublishingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Type",
                table: "Notifications");

            migrationBuilder.AddColumn<DateTime>(
                name: "MediaContainerCreatedAtUtc",
                table: "PublicationJobs",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PublicationJobs_OrganizationId_Id",
                table: "PublicationJobs",
                columns: new[] { "OrganizationId", "Id" });

            migrationBuilder.CreateTable(
                name: "PublicationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublicationJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WorkerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ResultState = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ErrorCategory = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProviderRequestId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationAttempts", x => x.Id);
                    table.CheckConstraint("CK_PublicationAttempts_ErrorCategory", "[ErrorCategory] IN ('RETRYABLE', 'ACTION_REQUIRED', 'UNCERTAIN', 'PERMANENT')");
                    table.CheckConstraint("CK_PublicationAttempts_ResultState", "[ResultState] IN ('SCHEDULED', 'PROCESSING', 'WAITING_MEDIA', 'RETRY_PENDING', 'ACTION_REQUIRED', 'RECONCILIATION_REQUIRED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'CANCELED')");
                    table.CheckConstraint("CK_PublicationAttempts_Step", "[Step] IN ('DISPATCH', 'MEDIA_STATUS')");
                    table.ForeignKey(
                        name: "FK_PublicationAttempts_PublicationJobs_OrganizationId_PublicationJobId",
                        columns: x => new { x.OrganizationId, x.PublicationJobId },
                        principalTable: "PublicationJobs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicationJobs_PublishedWasDispatched",
                table: "PublicationJobs",
                sql: "[DeliveryState] <> 'PUBLISHED' OR [DispatchStartedAtUtc] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Type",
                table: "Notifications",
                sql: "[Type] IN ('REVIEW_REQUESTED', 'CHANGES_REQUESTED', 'APPROVED', 'SCHEDULED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'ACTION_REQUIRED', 'REAUTHORIZATION_REQUIRED', 'RECONCILIATION_REQUIRED')");

            migrationBuilder.CreateIndex(
                name: "IX_PublicationAttempts_OrganizationId_PublicationJobId",
                table: "PublicationAttempts",
                columns: new[] { "OrganizationId", "PublicationJobId" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationAttempts_PublicationJobId_StartedAtUtc",
                table: "PublicationAttempts",
                columns: new[] { "PublicationJobId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicationAttempts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PublicationJobs_OrganizationId_Id",
                table: "PublicationJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicationJobs_PublishedWasDispatched",
                table: "PublicationJobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Type",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "MediaContainerCreatedAtUtc",
                table: "PublicationJobs");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Type",
                table: "Notifications",
                sql: "[Type] IN ('REVIEW_REQUESTED', 'CHANGES_REQUESTED', 'APPROVED', 'SCHEDULED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'ACTION_REQUIRED', 'REAUTHORIZATION_REQUIRED')");
        }
    }
}
