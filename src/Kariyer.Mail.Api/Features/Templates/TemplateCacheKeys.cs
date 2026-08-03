using StackExchange.Redis;

namespace Kariyer.Mail.Api.Features.Templates;

/// <summary>
/// Garnet keys for the template list. The version segment must be bumped whenever
/// <c>TemplateSummaryDto</c> gains or loses a field — a cached blob from the previous shape would
/// otherwise deserialize with nulls on non-nullable members for the whole TTL.
/// </summary>
internal static class TemplateCacheKeys
{
    private const string Version = "v2";

    public static string AllTemplates(bool includeArchived) =>
        $"templates:all:{Version}:archived_{(includeArchived ? "true" : "false")}";

    /// <summary>Drops both list variants. Call after any mutation that changes template rows.</summary>
    public static Task InvalidateListsAsync(IDatabase garnet) =>
        garnet.KeyDeleteAsync([AllTemplates(false), AllTemplates(true)]);
}
