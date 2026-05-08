using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kariyer.Mail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSlugToEmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                schema: "mail",
                table: "EmailTemplates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Partial unique index: enforces uniqueness only among rows where Slug IS NOT NULL
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_EmailTemplates_Slug\" ON mail.\"EmailTemplates\" (\"Slug\") WHERE \"Slug\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS mail.\"IX_EmailTemplates_Slug\";");

            migrationBuilder.DropColumn(
                name: "Slug",
                schema: "mail",
                table: "EmailTemplates");
        }
    }
}
