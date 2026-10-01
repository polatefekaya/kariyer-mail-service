using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kariyer.Mail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingStageMails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingStageMails",
                schema: "mail",
                columns: table => new
                {
                    ApplicationUid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MessageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ToStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SettingsKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RecipientUserId = table.Column<string>(type: "text", nullable: true),
                    RecipientEmail = table.Column<string>(type: "text", nullable: true),
                    TemplateData = table.Column<string>(type: "jsonb", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingStageMails", x => x.ApplicationUid);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingStageMails_Status_DueAt",
                schema: "mail",
                table: "PendingStageMails",
                columns: new[] { "Status", "DueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingStageMails",
                schema: "mail");
        }
    }
}
