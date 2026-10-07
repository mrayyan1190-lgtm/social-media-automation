using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialMediaAutomation.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MetaConfigurationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfigurationId",
                table: "SocialProviderConfigurations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfigurationId",
                table: "SocialProviderConfigurations");
        }
    }
}
