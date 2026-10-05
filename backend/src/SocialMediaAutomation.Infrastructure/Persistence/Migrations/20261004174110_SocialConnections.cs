using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SocialConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Capabilities",
                table: "SocialAccounts",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastVerifiedAtUtc",
                table: "SocialAccounts",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OAuthConnectionId",
                table: "SocialAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OAuthConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExternalUserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    AccessTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Scopes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConnectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthConnections", x => x.Id);
                    table.UniqueConstraint("AK_OAuthConnections_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_OAuthConnections_Platform", "[Platform] IN ('LinkedIn', 'Facebook', 'Instagram')");
                    table.CheckConstraint("CK_OAuthConnections_Scopes_Json", "ISJSON([Scopes]) = 1");
                    table.CheckConstraint("CK_OAuthConnections_Status", "[Status] IN ('ACTIVE', 'EXPIRED', 'REVOKED')");
                    table.ForeignKey(
                        name: "FK_OAuthConnections_OrganizationMembers_OrganizationId_ConnectedByUserId",
                        columns: x => new { x.OrganizationId, x.ConnectedByUserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OAuthConnections_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OAuthStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StateHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthStates", x => x.Id);
                    table.CheckConstraint("CK_OAuthStates_Platform", "[Platform] IN ('LinkedIn', 'Facebook', 'Instagram')");
                    table.CheckConstraint("CK_OAuthStates_RequestedMode", "[RequestedMode] IN ('LIVE', 'TEST_ACCOUNT', 'SIMULATED', 'NOT_CONFIGURED')");
                    table.ForeignKey(
                        name: "FK_OAuthStates_OrganizationMembers_OrganizationId_UserId",
                        columns: x => new { x.OrganizationId, x.UserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_OrganizationId_OAuthConnectionId",
                table: "SocialAccounts",
                columns: new[] { "OrganizationId", "OAuthConnectionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_OrganizationId_Platform_ExternalAccountId",
                table: "SocialAccounts",
                columns: new[] { "OrganizationId", "Platform", "ExternalAccountId" },
                unique: true,
                filter: "[ExternalAccountId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SocialAccounts_Capabilities_Json",
                table: "SocialAccounts",
                sql: "ISJSON([Capabilities]) = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SocialAccounts_ModeMatchesGrant",
                table: "SocialAccounts",
                sql: "([ExecutionMode] IN ('LIVE', 'TEST_ACCOUNT') AND [OAuthConnectionId] IS NOT NULL AND [ExternalAccountId] IS NOT NULL) OR ([ExecutionMode] IN ('SIMULATED', 'NOT_CONFIGURED') AND [OAuthConnectionId] IS NULL AND [ExternalAccountId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthConnections_OrganizationId_ConnectedByUserId",
                table: "OAuthConnections",
                columns: new[] { "OrganizationId", "ConnectedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OAuthConnections_OrganizationId_Platform_ExternalUserId",
                table: "OAuthConnections",
                columns: new[] { "OrganizationId", "Platform", "ExternalUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthStates_OrganizationId_UserId",
                table: "OAuthStates",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OAuthStates_StateHash",
                table: "OAuthStates",
                column: "StateHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialAccounts_OAuthConnections_OrganizationId_OAuthConnectionId",
                table: "SocialAccounts",
                columns: new[] { "OrganizationId", "OAuthConnectionId" },
                principalTable: "OAuthConnections",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialAccounts_OAuthConnections_OrganizationId_OAuthConnectionId",
                table: "SocialAccounts");

            migrationBuilder.DropTable(
                name: "OAuthConnections");

            migrationBuilder.DropTable(
                name: "OAuthStates");

            migrationBuilder.DropIndex(
                name: "IX_SocialAccounts_OrganizationId_OAuthConnectionId",
                table: "SocialAccounts");

            migrationBuilder.DropIndex(
                name: "IX_SocialAccounts_OrganizationId_Platform_ExternalAccountId",
                table: "SocialAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SocialAccounts_Capabilities_Json",
                table: "SocialAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SocialAccounts_ModeMatchesGrant",
                table: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "Capabilities",
                table: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "LastVerifiedAtUtc",
                table: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "OAuthConnectionId",
                table: "SocialAccounts");
        }
    }
}
