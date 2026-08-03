using System.Reflection;
using Kariyer.Mail.Api.Common.Configuration;
using Microsoft.Extensions.Options;

namespace Kariyer.Mail.Api.Features.Templates;

/// <summary>A system slot with its configured slug resolved from <see cref="EmailTemplateSettings"/>.</summary>
public sealed record SystemSlotDescriptor(string Context, string Description, string SettingsKey, string Slug);

public interface ITemplateContextResolver
{
    /// <summary>All system slots in display order, with their configured slug (may be empty).</summary>
    IReadOnlyList<SystemSlotDescriptor> Slots { get; }

    /// <summary>
    /// The authoring context a template belongs to, derived from its slug. Templates without a
    /// slug — or with a slug no slot claims — are admin-sent, i.e. <c>BulkEmail</c>.
    /// </summary>
    string ResolveContext(string? slug);

    /// <summary>The configured slug for a settings key, or null if the key is unknown/unconfigured.</summary>
    string? ResolveSlug(string? settingsKey);

    /// <summary>The definition for a context, or null if the context is unknown.</summary>
    TemplateContextDefinition? GetDefinition(string? context);

    /// <summary>Example values for every placeholder in a context — the preview's seed data.</summary>
    IReadOnlyDictionary<string, object?> GetExampleData(string? context);
}

internal sealed class TemplateContextResolver : ITemplateContextResolver
{
    private readonly Dictionary<string, string> _contextBySlug;
    private readonly Dictionary<string, string> _slugBySettingsKey;
    private readonly Dictionary<string, IReadOnlyDictionary<string, object?>> _exampleDataByContext;

    public IReadOnlyList<SystemSlotDescriptor> Slots { get; }

    public TemplateContextResolver(IOptions<EmailTemplateSettings> options, ILogger<TemplateContextResolver> logger)
    {
        EmailTemplateSettings settings = options.Value;

        Slots = TemplateContextRegistry.SystemSlots
            .Select(d => new SystemSlotDescriptor(
                d.Context,
                d.Description,
                d.SettingsKey!,
                d.SlugAccessor!(settings) ?? string.Empty))
            .ToArray();

        _slugBySettingsKey = Slots.ToDictionary(s => s.SettingsKey, s => s.Slug, StringComparer.Ordinal);

        _contextBySlug = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SystemSlotDescriptor slot in Slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Slug)) continue;

            // Two slots sharing a slug is a misconfiguration: the Slug column is uniquely indexed,
            // so only one of them could ever be assigned. First one wins; warn loudly.
            if (_contextBySlug.TryGetValue(slot.Slug, out string? existing))
            {
                logger.LogWarning(
                    "Slug [{Slug}] is configured for both [{Existing}] and [{Duplicate}]. " +
                    "Only [{Existing}] will resolve — fix the EmailTemplates configuration.",
                    slot.Slug, existing, slot.Context, existing);
                continue;
            }

            _contextBySlug[slot.Slug] = slot.Context;
        }

        _exampleDataByContext = TemplateContextRegistry.All.ToDictionary(
            d => d.Context,
            d => (IReadOnlyDictionary<string, object?>)d.Placeholders.ToDictionary(
                p => p.Name, p => (object?)p.Example, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    public string ResolveContext(string? slug) =>
        slug is not null && _contextBySlug.TryGetValue(slug, out string? context)
            ? context
            : TemplateContextRegistry.BulkEmailContext;

    public string? ResolveSlug(string? settingsKey) =>
        settingsKey is not null && _slugBySettingsKey.TryGetValue(settingsKey, out string? slug) ? slug : null;

    public TemplateContextDefinition? GetDefinition(string? context) =>
        TemplateContextRegistry.TryGetByContext(context, out TemplateContextDefinition definition) ? definition : null;

    public IReadOnlyDictionary<string, object?> GetExampleData(string? context) =>
        context is not null && _exampleDataByContext.TryGetValue(context, out IReadOnlyDictionary<string, object?>? data)
            ? data
            : _exampleDataByContext[TemplateContextRegistry.BulkEmailContext];

    /// <summary>
    /// Fails startup if <see cref="TemplateContextRegistry"/> and <see cref="EmailTemplateSettings"/>
    /// have drifted apart. Catching this at boot is the whole point of collapsing the four hardcoded
    /// lists into one — a mismatch here is what silently produced empty placeholder sets before.
    /// </summary>
    public static void AssertRegistryMatchesSettings()
    {
        string[] settingsProperties = typeof(EmailTemplateSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToArray();

        string[] registryKeys = TemplateContextRegistry.SystemSlots.Select(d => d.SettingsKey!).ToArray();

        string[] unknown = registryKeys.Except(settingsProperties, StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException(
                $"TemplateContextRegistry references settings keys that do not exist on EmailTemplateSettings: {string.Join(", ", unknown)}");

        string[] uncovered = settingsProperties.Except(registryKeys, StringComparer.Ordinal).ToArray();
        if (uncovered.Length > 0)
            throw new InvalidOperationException(
                $"EmailTemplateSettings has slug properties with no slot in TemplateContextRegistry: {string.Join(", ", uncovered)}");

        string[] duplicateContexts = TemplateContextRegistry.All
            .GroupBy(d => d.Context, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateContexts.Length > 0)
            throw new InvalidOperationException(
                $"TemplateContextRegistry declares duplicate contexts: {string.Join(", ", duplicateContexts)}");

        string[] emptyVocabularies = TemplateContextRegistry.All
            .Where(d => d.Placeholders.Count == 0)
            .Select(d => d.Context)
            .ToArray();

        if (emptyVocabularies.Length > 0)
            throw new InvalidOperationException(
                $"TemplateContextRegistry declares contexts with no placeholders: {string.Join(", ", emptyVocabularies)}");
    }
}
