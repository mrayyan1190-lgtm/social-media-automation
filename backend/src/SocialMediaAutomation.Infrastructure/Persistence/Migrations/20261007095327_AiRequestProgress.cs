using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiRequestProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Stage",
                table: "AiGenerationRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Warning",
                table: "AiGenerationRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AiGenerationRequests_Stage",
                table: "AiGenerationRequests",
                sql: "[Stage] IN ('ANALYZING_IMAGES', 'WRITING', 'GENERATING_IMAGE')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AiGenerationRequests_Stage",
                table: "AiGenerationRequests");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "AiGenerationRequests");

            migrationBuilder.DropColumn(
                name: "Warning",
                table: "AiGenerationRequests");
        }
    }
}
