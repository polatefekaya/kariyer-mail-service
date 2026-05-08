using Kariyer.Mail.Api.Common.Models;

namespace Kariyer.Mail.Api.Features.Templates;

internal interface ITemplateResolutionService
{
    Task<EmailTemplate?> GetTemplateAsync(Ulid templateId, CancellationToken ct = default);
    Task<EmailTemplate?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task InvalidateAsync(Ulid id, string? slug = null);
}
