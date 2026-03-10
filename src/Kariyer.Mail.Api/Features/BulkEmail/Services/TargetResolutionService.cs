using System.Diagnostics;
using System.Text.Json;
using Kariyer.Mail.Api.Common.Telemetry;

namespace Kariyer.Mail.Api.Features.BulkEmail.Services;

internal sealed class TargetResolutionService : ITargetResolutionService
{
    private readonly HttpClient _legacyClient;
    private readonly ILogger<TargetResolutionService> _logger;

    public TargetResolutionService(
        HttpClient legacyClient,
        ILogger<TargetResolutionService> logger)
    {
        _legacyClient = legacyClient;
        _logger = logger;
    }

    public async Task<List<ResolvedTarget>> ResolveTargetsAsync(JsonDocument filters, int page, int pageSize, CancellationToken ct)
    {
        using Activity? activity = DiagnosticsConfig.MailActivitySource.StartActivity("LegacyTargetResolution");
        activity?.SetTag("legacy.request.page", page);
        activity?.SetTag("legacy.request.page_size", pageSize);

        string relativeUri = $"targets/resolve?page={page}&pageSize={pageSize}";

        _logger.LogDebug("Calling Legacy API at {RequestUri} to resolve targets. Page: {Page}", relativeUri, page);

        HttpResponseMessage response = await _legacyClient.PostAsJsonAsync(relativeUri, filters, ct);

        if (!response.IsSuccessStatusCode)
        {
            string errorContent = await response.Content.ReadAsStringAsync(ct);

            activity?.SetStatus(ActivityStatusCode.Error, $"Legacy API failed with {response.StatusCode}");
            activity?.SetTag("legacy.error_content", errorContent);

            _logger.LogError("Legacy API resolution failed. StatusCode: {StatusCode}, Error: {ErrorContent}",
                response.StatusCode, errorContent);

            response.EnsureSuccessStatusCode();
        }

        List<ResolvedTarget>? targets = await response.Content.ReadFromJsonAsync<List<ResolvedTarget>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken: ct
        );
        
        int count = targets?.Count ?? 0;
        activity?.SetTag("legacy.response.count", count);

        _logger.LogDebug("Legacy API successfully returned {TargetCount} targets for Page {Page}", count, page);

        return targets ?? new List<ResolvedTarget>();
    }
}