using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <summary>Allows "Auto Detect" (AUTO) as a campaign objective and call to action, and the TEAM_INVITATION notification type.</summary>
    [DbContext(typeof(CoreDbContext))]
    [Migration("20261005160000_CustomerExperience")]
    public partial class CustomerExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "CK_Notifications_Type", table: "Notifications");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Type",
                table: "Notifications",
                sql: "[Type] IN ('REVIEW_REQUESTED', 'CHANGES_REQUESTED', 'APPROVED', 'SCHEDULED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'ACTION_REQUIRED', 'REAUTHORIZATION_REQUIRED', 'RECONCILIATION_REQUIRED', 'TEAM_INVITATION')");
            migrationBuilder.DropCheckConstraint(name: "CK_Campaigns_CallToAction", table: "Campaigns");
            migrationBuilder.DropCheckConstraint(name: "CK_Campaigns_Objective", table: "Campaigns");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_CallToAction",
                table: "Campaigns",
                sql: "[CallToAction] IN ('NONE', 'LEARN_MORE', 'VISIT_WEBSITE', 'BOOK_DEMO', 'SIGN_UP', 'CONTACT_US', 'REGISTER_NOW', 'DOWNLOAD', 'AUTO')");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_Objective",
                table: "Campaigns",
                sql: "[Objective] IN ('BRAND_AWARENESS', 'PRODUCT_LAUNCH', 'LEAD_GENERATION', 'ENGAGEMENT', 'WEBSITE_TRAFFIC', 'ANNOUNCEMENT', 'EDUCATIONAL', 'AUTO')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [Notifications] WHERE [Type] = 'TEAM_INVITATION'");
            migrationBuilder.DropCheckConstraint(name: "CK_Notifications_Type", table: "Notifications");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Type",
                table: "Notifications",
                sql: "[Type] IN ('REVIEW_REQUESTED', 'CHANGES_REQUESTED', 'APPROVED', 'SCHEDULED', 'PUBLISHED', 'SIMULATED', 'FAILED', 'ACTION_REQUIRED', 'REAUTHORIZATION_REQUIRED', 'RECONCILIATION_REQUIRED')");
            migrationBuilder.DropCheckConstraint(name: "CK_Campaigns_CallToAction", table: "Campaigns");
            migrationBuilder.DropCheckConstraint(name: "CK_Campaigns_Objective", table: "Campaigns");
            migrationBuilder.Sql("UPDATE [Campaigns] SET [Objective] = 'BRAND_AWARENESS' WHERE [Objective] = 'AUTO'");
            migrationBuilder.Sql("UPDATE [Campaigns] SET [CallToAction] = 'NONE' WHERE [CallToAction] = 'AUTO'");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_CallToAction",
                table: "Campaigns",
                sql: "[CallToAction] IN ('NONE', 'LEARN_MORE', 'VISIT_WEBSITE', 'BOOK_DEMO', 'SIGN_UP', 'CONTACT_US', 'REGISTER_NOW', 'DOWNLOAD')");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_Objective",
                table: "Campaigns",
                sql: "[Objective] IN ('BRAND_AWARENESS', 'PRODUCT_LAUNCH', 'LEAD_GENERATION', 'ENGAGEMENT', 'WEBSITE_TRAFFIC', 'ANNOUNCEMENT', 'EDUCATIONAL')");
        }
    }
}
