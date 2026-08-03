using System.Text.Json;
using Kariyer.Mail.Api.Common.Templating;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

public enum TemplatePreviewErrorKind
{
    None,
    EmptyContent,
    UnknownContext,
    SyntaxError,
    RuntimeError
}

public sealed record TemplatePreviewResult(
    TemplatePreviewErrorKind ErrorKind,
    string? Message,
    IReadOnlyList<TemplateIssue> Issues,
    string RenderedSubject,
    string RenderedHtml,
    string Context,
    IReadOnlyList<string> UnknownVariables,
    IReadOnlyList<string> UsedVariables)
{
    public bool Succeeded => ErrorKind == TemplatePreviewErrorKind.None;

    public static TemplatePreviewResult Failure(TemplatePreviewErrorKind kind, string message, string context, IReadOnlyList<TemplateIssue>? issues = null) =>
        new(kind, message, issues ?? [], string.Empty, string.Empty, context, [], []);
}

public interface ITemplatePreviewService
{
    Task<TemplatePreviewResult> RenderAsync(
        string? subject,
        string? html,
        string? context,
        IReadOnlyDictionary<string, object?>? overrides,
        CancellationToken ct);
}

/// <summary>
/// The single renderer behind both preview endpoints.
///
/// Preview used to run with <c>StrictVariables = true</c> and only whatever dummy data the browser
/// happened to send, so a template referencing a variable the client didn't know about aborted on
/// the first miss and came back as a 400 the UI displayed as "Syntax hatası." — which is why every
/// automated template looked broken. Now the server seeds the full vocabulary for the context and
/// collects *all* unresolved variables in one pass, so a missing variable is information, not an
/// error. Only genuinely malformed templates fail.
/// </summary>
internal sealed class TemplatePreviewService(ITemplateContextResolver contextResolver) : ITemplatePreviewService
{
    public async Task<TemplatePreviewResult> RenderAsync(
        string? subject,
        string? html,
        string? context,
        IReadOnlyDictionary<string, object?>? overrides,
        CancellationToken ct)
    {
        string resolvedContext = context ?? TemplateContextRegistry.BulkEmailContext;

        if (contextResolver.GetDefinition(resolvedContext) is null)
        {
            return TemplatePreviewResult.Failure(
                TemplatePreviewErrorKind.UnknownContext,
                $"Bilinmeyen şablon bağlamı: '{resolvedContext}'.",
                resolvedContext);
        }

        string normalizedSubject = ScribanContentNormalizer.Normalize(subject);
        string normalizedHtml = ScribanContentNormalizer.Normalize(html);

        if (normalizedSubject.Length == 0 && normalizedHtml.Length == 0)
        {
            return TemplatePreviewResult.Failure(
                TemplatePreviewErrorKind.EmptyContent,
                "Önizlenecek içerik yok.",
                resolvedContext);
        }

        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(normalizedSubject, normalizedHtml, knownVariables: null);

        if (analysis.HasErrors)
        {
            return TemplatePreviewResult.Failure(
                TemplatePreviewErrorKind.SyntaxError,
                "Şablonda sözdizimi hatası var.",
                resolvedContext,
                analysis.Errors);
        }

        ScriptObject scriptObject = BuildModel(resolvedContext, overrides);

        HashSet<string> unresolved = new(StringComparer.Ordinal);

        TemplateContext templateContext = new()
        {
            MemberRenamer = member => member.Name,   // keep PascalCase; Scriban would snake_case it
            StrictVariables = false
        };
        templateContext.TryGetVariable = (TemplateContext _, SourceSpan _, ScriptVariable variable, out object? value) =>
        {
            unresolved.Add(variable.Name);
            value = string.Empty;
            return true;                             // resolve to empty, exactly like the send path
        };
        templateContext.PushGlobal(scriptObject);

        try
        {
            ct.ThrowIfCancellationRequested();

            string renderedSubject = await Template.Parse(normalizedSubject).RenderAsync(templateContext);
            string renderedHtml = await Template.Parse(normalizedHtml).RenderAsync(templateContext);

            string[] unknown = unresolved.OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] used = analysis.ReferencedVariables
                .Where(n => !unresolved.Contains(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            return new TemplatePreviewResult(
                TemplatePreviewErrorKind.None,
                Message: null,
                Issues: [],
                renderedSubject,
                renderedHtml,
                resolvedContext,
                unknown,
                used);
        }
        catch (ScriptRuntimeException ex)
        {
            TemplateIssue issue = new(
                ScribanTemplateAnalyzer.BodyField,
                "RuntimeError",
                ex.OriginalMessage,
                ex.Span.Start.Line + 1,
                ex.Span.Start.Column + 1);

            return TemplatePreviewResult.Failure(
                TemplatePreviewErrorKind.RuntimeError,
                "Şablon işlenirken çalışma zamanı hatası oluştu.",
                resolvedContext,
                [issue]);
        }
    }

    /// <summary>
    /// The context's example data, overlaid with anything the client explicitly sent. Seeding from
    /// the server means the editor cannot produce a "missing variable" for a variable the context
    /// genuinely supplies.
    /// </summary>
    private ScriptObject BuildModel(string context, IReadOnlyDictionary<string, object?>? overrides)
    {
        ScriptObject scriptObject = new();

        foreach ((string key, object? value) in contextResolver.GetExampleData(context))
            scriptObject[key] = value;

        if (overrides is not null)
        {
            foreach ((string key, object? value) in overrides)
                scriptObject[key] = UnwrapJsonElement(value);
        }

        return scriptObject;
    }

    /// <summary>JSON values arrive as <see cref="JsonElement"/>; Scriban wants CLR primitives.</summary>
    public static object? UnwrapJsonElement(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.String } el => el.GetString(),
        JsonElement { ValueKind: JsonValueKind.True }      => true,
        JsonElement { ValueKind: JsonValueKind.False }     => false,
        JsonElement { ValueKind: JsonValueKind.Null }      => null,
        JsonElement { ValueKind: JsonValueKind.Number } el =>
            el.TryGetInt64(out long l) ? l : el.GetDouble(),
        _ => value
    };
}
