using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Common.Models;
using Kariyer.Mail.Api.Common.Persistence;
using Kariyer.Mail.Api.Common.Web;
using Kariyer.Mail.Api.Features.Templates.GetTemplate.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Templates.GetSystemTemplates;

internal sealed class GetSystemTemplatesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("templates/system", async (
            MailDbContext dbContext,
            IOptions<EmailTemplateSettings> settingsOptions,
            CancellationToken ct) =>
        {
            EmailTemplateSettings s = settingsOptions.Value;

            // All defined system slots: (context label, settings key, raw ID string from config)
            (string Context, string Description, string SettingsKey, string RawId)[] slots =
            [
                ("AccountCreated",              "Yeni bir hesap oluşturulduğunda gönderilir.",                        nameof(s.AccountCreatedTemplateId),              s.AccountCreatedTemplateId),
                ("AccountCompleted",            "Kullanıcı profilini tamamladığında gönderilir.",                     nameof(s.AccountCompletedTemplateId),            s.AccountCompletedTemplateId),
                ("AccountApproved",             "Hesap başvurusu onaylandığında gönderilir.",                         nameof(s.AccountApprovedTemplateId),             s.AccountApprovedTemplateId),
                ("AccountRejected",             "Hesap başvurusu reddedildiğinde gönderilir.",                        nameof(s.AccountRejectedTemplateId),             s.AccountRejectedTemplateId),
                ("AccountFrozen",               "Hesap dondurulduğunda gönderilir.",                                  nameof(s.AccountFrozenTemplateId),               s.AccountFrozenTemplateId),
                ("AccountDeleted",              "Hesap silindiğinde gönderilir.",                                     nameof(s.AccountDeletedTemplateId),              s.AccountDeletedTemplateId),
                ("AccountDidNotCompleted.Step1","1. hatırlatma: Kullanıcı profili tamamlanmamış.",                    nameof(s.AccountDidNotCompletedStep1TemplateId), s.AccountDidNotCompletedStep1TemplateId),
                ("AccountDidNotCompleted.Step2","2. hatırlatma: Kullanıcı profili tamamlanmamış.",                    nameof(s.AccountDidNotCompletedStep2TemplateId), s.AccountDidNotCompletedStep2TemplateId),
                ("AccountDidNotCompleted.Step3","3. hatırlatma: Kullanıcı profili tamamlanmamış.",                    nameof(s.AccountDidNotCompletedStep3TemplateId), s.AccountDidNotCompletedStep3TemplateId),
                ("AdminCompanyCompleted",       "Bir şirket profilini tamamladığında yöneticiye bildirim gönderilir.", nameof(s.AdminCompanyCompletedTemplateId),        s.AdminCompanyCompletedTemplateId),
            ];

            // Parse all non-empty IDs so we can do a single bulk DB query
            Dictionary<string, Ulid> parsedIds = slots
                .Where(slot => !string.IsNullOrWhiteSpace(slot.RawId) && Ulid.TryParse(slot.RawId, out _))
                .ToDictionary(
                    slot => slot.Context,
                    slot => Ulid.Parse(slot.RawId));

            // Single query for all referenced templates
            Ulid[] templateIds = [.. parsedIds.Values.Distinct()];

            Dictionary<Ulid, EmailTemplate> templates = await dbContext.EmailTemplates
                .AsNoTracking()
                .Where(t => templateIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, ct);

            List<SystemTemplateSlotDto> result = new(slots.Length);

            foreach (var (context, description, settingsKey, rawId) in slots)
            {
                if (string.IsNullOrWhiteSpace(rawId))
                {
                    result.Add(new(context, description, settingsKey, SystemTemplateStatus.Empty, null));
                    continue;
                }

                if (!parsedIds.TryGetValue(context, out Ulid templateId))
                {
                    // Raw ID exists but couldn't be parsed as a Ulid — treat as misconfigured
                    result.Add(new(context, description, settingsKey, SystemTemplateStatus.NotFound, null));
                    continue;
                }

                if (!templates.TryGetValue(templateId, out EmailTemplate? template))
                {
                    result.Add(new(context, description, settingsKey, SystemTemplateStatus.NotFound, null));
                    continue;
                }

                TemplateDetailDto dto = new(
                    template.Id,
                    template.Name,
                    template.SubjectTemplate,
                    template.HtmlContent,
                    template.IsArchived,
                    template.IsSystemTemplate,
                    template.CreatedAt,
                    template.UpdatedAt);

                result.Add(new(context, description, settingsKey, SystemTemplateStatus.Configured, dto));
            }

            return Results.Ok(result);
        })
        .WithTags("Templates");
    }
}
