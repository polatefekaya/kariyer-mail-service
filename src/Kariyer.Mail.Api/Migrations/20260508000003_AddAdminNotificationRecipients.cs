using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kariyer.Mail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminNotificationRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminNotificationRecipients",
                schema: "mail",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(26)", maxLength: 26, nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminNotificationRecipients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminNotificationRecipients_Email",
                schema: "mail",
                table: "AdminNotificationRecipients",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminNotificationRecipients",
                schema: "mail");
        }
    }
}
