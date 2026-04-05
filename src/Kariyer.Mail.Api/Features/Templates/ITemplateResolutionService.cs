using Kariyer.Mail.Api.Common.Models;

namespace Kariyer.Mail.Api.Features.Templates;

internal interface ITemplateResolutionService
{
    Task<EmailTemplate?> GetTemplateAsync(Ulid templateId, CancellationToken cancellationToken = default);
    Task InvalidateTemplateCacheAsync(Ulid templateId);
}