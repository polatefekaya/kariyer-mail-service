using System.Text.Json;

namespace Kariyer.Mail.Api.Features.BulkEmail.Services;

public interface ITargetResolutionService
{
    public Task<List<ResolvedTarget>> ResolveTargetsAsync(JsonDocument filters, int page, int pageSize, CancellationToken ct);
}