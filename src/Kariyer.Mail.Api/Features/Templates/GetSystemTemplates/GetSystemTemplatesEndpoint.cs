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

            (string Context, string Description, string SettingsKey, string Slug)[] slots =
            [
                ("AccountCreated",              "Yeni bir hesap oluşturulduğunda gönderilir.",                        nameof(s.AccountCreatedTemplateSlug),              s.AccountCreatedTemplateSlug),
                ("AccountCompleted",            "Kullanıcı profilini tamamladığında gönderilir.",                     nameof(s.AccountCompletedTemplateSlug),            s.AccountCompletedTemplateSlug),
                ("AccountApproved",             "Hesap başvurusu onaylandığında gönderilir.",                         nameof(s.AccountApprovedTemplateSlug),             s.AccountApprovedTemplateSlug),
                ("AccountRejected",             "Hesap başvurusu reddedildiğinde gönderilir.",                        nameof(s.AccountRejectedTemplateSlug),             s.AccountRejectedTemplateSlug),
                ("AccountFrozen",               "Hesap dondurulduğunda gönderilir.",                                  nameof(s.AccountFrozenTemplateSlug),               s.AccountFrozenTemplateSlug),
                ("AccountDeleted",              "Hesap silindiğinde gönderilir.",                                     nameof(s.AccountDeletedTemplateSlug),              s.AccountDeletedTemplateSlug),
                ("AccountDidNotCompleted.Step1","1. hatırlatma: Kullanıcı profili tamamlanmamış.",                    nameof(s.AccountDidNotCompletedStep1TemplateSlug), s.AccountDidNotCompletedStep1TemplateSlug),
                ("AccountDidNotCompleted.Step2","2. hatırlatma: Kullanıcı profili tamamlanmamış.",                    nameof(s.AccountDidNotCompletedStep2TemplateSlug), s.AccountDidNotCompletedStep2TemplateSlug),
                ("AccountDidNotCompleted.Step3","3. hatırlatma: Kullanıcı profili tamamlanmamış.",                    nameof(s.AccountDidNotCompletedStep3TemplateSlug), s.AccountDidNotCompletedStep3TemplateSlug),
                ("AdminCompanyCompleted",       "Bir şirket profilini tamamladığında yöneticiye bildirim gönderilir.", nameof(s.AdminCompanyCompletedTemplateSlug),        s.AdminCompanyCompletedTemplateSlug),
                ("AccountDeletionCancelled",    "Hesap silme talebi iptal edildiğinde gönderilir.",                   nameof(s.AccountDeletionCancelledTemplateSlug),    s.AccountDeletionCancelledTemplateSlug),
                ("AccountEmailChanged",         "Hesap e-posta adresi değiştirildiğinde gönderilir.",                 nameof(s.AccountEmailChangedTemplateSlug),         s.AccountEmailChangedTemplateSlug),
                ("AccountPhoneChanged",         "Hesap telefon numarası değiştirildiğinde gönderilir.",               nameof(s.AccountPhoneChangedTemplateSlug),         s.AccountPhoneChangedTemplateSlug),
                ("AccountUsernameChanged",      "Hesap kullanıcı adı değiştirildiğinde gönderilir.",                  nameof(s.AccountUsernameChangedTemplateSlug),      s.AccountUsernameChangedTemplateSlug),
            ];

            // Collect all configured slugs for a single bulk DB query
            string[] slugs = slots
                .Where(slot => !string.IsNullOrWhiteSpace(slot.Slug))
                .Select(slot => slot.Slug)
                .Distinct()
                .ToArray();

            Dictionary<string, EmailTemplate> templates = await dbContext.EmailTemplates
                .AsNoTracking()
                .Where(t => t.Slug != null && slugs.Contains(t.Slug))
                .ToDictionaryAsync(t => t.Slug!, ct);

            List<SystemTemplateSlotDto> result = new(slots.Length);

            foreach (var (context, description, settingsKey, slug) in slots)
            {
                if (string.IsNullOrWhiteSpace(slug))
                {
                    result.Add(new(context, description, settingsKey, SystemTemplateStatus.Empty, null));
                    continue;
                }

                if (!templates.TryGetValue(slug, out EmailTemplate? template))
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
                    template.Slug,
                    template.CreatedAt,
                    template.UpdatedAt);

                result.Add(new(context, description, settingsKey, SystemTemplateStatus.Configured, dto));
            }

            return Results.Ok(result);
        })
        .WithTags("Templates");
    }
}
