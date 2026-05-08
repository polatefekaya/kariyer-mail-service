using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kariyer.Mail.Api.Migrations
{
    public partial class EnsureMissingColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE mail.""EmailTemplates""
                    ADD COLUMN IF NOT EXISTS ""IsSystemTemplate"" boolean NOT NULL DEFAULT false;

                ALTER TABLE mail.""EmailTemplates""
                    ADD COLUMN IF NOT EXISTS ""Slug"" character varying(100);

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_EmailTemplates_Slug""
                    ON mail.""EmailTemplates"" (""Slug"")
                    WHERE ""Slug"" IS NOT NULL;

                CREATE TABLE IF NOT EXISTS mail.""AdminNotificationRecipients"" (
                    ""Id""        character varying(26) NOT NULL,
                    ""Email""     text                  NOT NULL,
                    ""Label""     text,
                    ""IsActive""  boolean               NOT NULL DEFAULT true,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone,
                    CONSTRAINT ""PK_AdminNotificationRecipients"" PRIMARY KEY (""Id"")
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AdminNotificationRecipients_Email""
                    ON mail.""AdminNotificationRecipients"" (""Email"");
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS mail.""IX_AdminNotificationRecipients_Email"";
                DROP TABLE IF EXISTS mail.""AdminNotificationRecipients"";
                DROP INDEX IF EXISTS mail.""IX_EmailTemplates_Slug"";
                ALTER TABLE mail.""EmailTemplates"" DROP COLUMN IF EXISTS ""Slug"";
                ALTER TABLE mail.""EmailTemplates"" DROP COLUMN IF EXISTS ""IsSystemTemplate"";
            ");
        }
    }
}
