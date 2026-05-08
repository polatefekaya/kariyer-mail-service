using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kariyer.Mail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsSystemTemplateToEmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystemTemplate",
                schema: "mail",
                table: "EmailTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSystemTemplate",
                schema: "mail",
                table: "EmailTemplates");
        }
    }
}
