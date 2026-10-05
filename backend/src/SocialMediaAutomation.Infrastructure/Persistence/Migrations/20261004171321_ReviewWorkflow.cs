using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_OrganizationId_Id_CurrentVersionId",
                table: "Posts");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.CheckConstraint("CK_AuditLogs_MetadataJson_Json", "ISJSON([MetadataJson]) = 1");
                    table.ForeignKey(
                        name: "FK_AuditLogs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Type", "[Type] IN ('REVIEW_REQUESTED', 'CHANGES_REQUESTED', 'APPROVED', 'SCHEDULED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'ACTION_REQUIRED', 'REAUTHORIZATION_REQUIRED')");
                    table.ForeignKey(
                        name: "FK_Notifications_OrganizationMembers_OrganizationId_RecipientUserId",
                        columns: x => new { x.OrganizationId, x.RecipientUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DecisionComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApprovedPayloadVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ApprovedPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedPayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewRequests", x => x.Id);
                    table.UniqueConstraint("AK_ReviewRequests_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.UniqueConstraint("AK_ReviewRequests_OrganizationId_PostId_PostVersionId_Id", x => new { x.OrganizationId, x.PostId, x.PostVersionId, x.Id });
                    table.CheckConstraint("CK_ReviewRequests_ApprovedPayload", "[Status] <> 'APPROVED' OR ([ApprovedPayloadHash] IS NOT NULL AND [ApprovedPayloadJson] IS NOT NULL AND [DecidedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_ReviewRequests_ApprovedPayloadJson_Json", "ISJSON([ApprovedPayloadJson]) = 1");
                    table.CheckConstraint("CK_ReviewRequests_ChangesExplained", "[Status] <> 'CHANGES_REQUESTED' OR LEN(LTRIM(RTRIM([DecisionComment]))) > 0");
                    table.CheckConstraint("CK_ReviewRequests_Status", "[Status] IN ('PENDING', 'APPROVED', 'CHANGES_REQUESTED')");
                    table.ForeignKey(
                        name: "FK_ReviewRequests_OrganizationMembers_OrganizationId_DecidedByUserId",
                        columns: x => new { x.OrganizationId, x.DecidedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewRequests_OrganizationMembers_OrganizationId_SubmittedByUserId",
                        columns: x => new { x.OrganizationId, x.SubmittedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewRequests_PostVersions_OrganizationId_PostId_PostVersionId",
                        columns: x => new { x.OrganizationId, x.PostId, x.PostVersionId },
                        principalTable: "PostVersions",
                        principalColumns: new[] { "OrganizationId", "PostId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewRequests_Posts_OrganizationId_PostId",
                        columns: x => new { x.OrganizationId, x.PostId },
                        principalTable: "Posts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SocialAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExternalAccountId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExecutionMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConnectionStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialAccounts", x => x.Id);
                    table.UniqueConstraint("AK_SocialAccounts_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_SocialAccounts_ConnectionStatus", "[ConnectionStatus] IN ('CONNECTED', 'REAUTHORIZATION_REQUIRED', 'DISCONNECTED')");
                    table.CheckConstraint("CK_SocialAccounts_ExecutionMode", "[ExecutionMode] IN ('LIVE', 'TEST_ACCOUNT', 'SIMULATED', 'NOT_CONFIGURED')");
                    table.CheckConstraint("CK_SocialAccounts_Platform", "[Platform] IN ('LinkedIn', 'Facebook', 'Instagram')");
                    table.ForeignKey(
                        name: "FK_SocialAccounts_OrganizationMembers_OrganizationId_CreatedByUserId",
                        columns: x => new { x.OrganizationId, x.CreatedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SocialAccounts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewComments_OrganizationMembers_OrganizationId_AuthorUserId",
                        columns: x => new { x.OrganizationId, x.AuthorUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewComments_ReviewRequests_OrganizationId_ReviewRequestId",
                        columns: x => new { x.OrganizationId, x.ReviewRequestId },
                        principalTable: "ReviewRequests",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostVersions_OrganizationId_SocialAccountId",
                table: "PostVersions",
                columns: new[] { "OrganizationId", "SocialAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_OrganizationId_Id_CurrentVersionId_ActiveApprovalId",
                table: "Posts",
                columns: new[] { "OrganizationId", "Id", "CurrentVersionId", "ActiveApprovalId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Posts_ApprovalMatchesState",
                table: "Posts",
                sql: "([EditorialState] = 'APPROVED' AND [ActiveApprovalId] IS NOT NULL) OR ([EditorialState] <> 'APPROVED' AND [ActiveApprovalId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorUserId",
                table: "AuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OrganizationId_CreatedAtUtc",
                table: "AuditLogs",
                columns: new[] { "OrganizationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OrganizationId_ResourceId",
                table: "AuditLogs",
                columns: new[] { "OrganizationId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrganizationId_RecipientUserId",
                table: "Notifications",
                columns: new[] { "OrganizationId", "RecipientUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_OrganizationId_ReadAtUtc_CreatedAtUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "OrganizationId", "ReadAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewComments_OrganizationId_AuthorUserId",
                table: "ReviewComments",
                columns: new[] { "OrganizationId", "AuthorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewComments_OrganizationId_ReviewRequestId",
                table: "ReviewComments",
                columns: new[] { "OrganizationId", "ReviewRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_OrganizationId_DecidedByUserId",
                table: "ReviewRequests",
                columns: new[] { "OrganizationId", "DecidedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_OrganizationId_Status_SubmittedAtUtc",
                table: "ReviewRequests",
                columns: new[] { "OrganizationId", "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRequests_OrganizationId_SubmittedByUserId",
                table: "ReviewRequests",
                columns: new[] { "OrganizationId", "SubmittedByUserId" });

            migrationBuilder.CreateIndex(
                name: "UX_ReviewRequests_OnePendingPerPost",
                table: "ReviewRequests",
                column: "PostId",
                unique: true,
                filter: "[Status] = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_OrganizationId_CreatedByUserId",
                table: "SocialAccounts",
                columns: new[] { "OrganizationId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_OrganizationId_Platform",
                table: "SocialAccounts",
                columns: new[] { "OrganizationId", "Platform" });

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_ReviewRequests_OrganizationId_Id_CurrentVersionId_ActiveApprovalId",
                table: "Posts",
                columns: new[] { "OrganizationId", "Id", "CurrentVersionId", "ActiveApprovalId" },
                principalTable: "ReviewRequests",
                principalColumns: new[] { "OrganizationId", "PostId", "PostVersionId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PostVersions_SocialAccounts_OrganizationId_SocialAccountId",
                table: "PostVersions",
                columns: new[] { "OrganizationId", "SocialAccountId" },
                principalTable: "SocialAccounts",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Audit history is append-only (AGENTS.md §52).
            // EXEC: CREATE TRIGGER must start its own batch, which the IF-wrapped idempotent deploy script otherwise breaks.
            migrationBuilder.Sql(@"EXEC(N'CREATE TRIGGER [dbo].[TR_AuditLogs_Immutable] ON [dbo].[AuditLogs] AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, ''AuditLogs are append-only.'', 1;
END')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_AuditLogs_Immutable]");
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_ReviewRequests_OrganizationId_Id_CurrentVersionId_ActiveApprovalId",
                table: "Posts");

            migrationBuilder.DropForeignKey(
                name: "FK_PostVersions_SocialAccounts_OrganizationId_SocialAccountId",
                table: "PostVersions");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "ReviewComments");

            migrationBuilder.DropTable(
                name: "SocialAccounts");

            migrationBuilder.DropTable(
                name: "ReviewRequests");

            migrationBuilder.DropIndex(
                name: "IX_PostVersions_OrganizationId_SocialAccountId",
                table: "PostVersions");

            migrationBuilder.DropIndex(
                name: "IX_Posts_OrganizationId_Id_CurrentVersionId_ActiveApprovalId",
                table: "Posts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Posts_ApprovalMatchesState",
                table: "Posts");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_OrganizationId_Id_CurrentVersionId",
                table: "Posts",
                columns: new[] { "OrganizationId", "Id", "CurrentVersionId" });
        }
    }
}
