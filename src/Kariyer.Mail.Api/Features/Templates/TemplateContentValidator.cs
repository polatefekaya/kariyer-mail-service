using Kariyer.Mail.Api.Common.Templating;

namespace Kariyer.Mail.Api.Features.Templates;

public sealed record TemplateValidationResponse(
    string Message,
    string ErrorKind,
    IReadOnlyList<TemplateIssue> Issues,
    IReadOnlyList<TemplateIssue> Warnings);

internal sealed record PreparedTemplateContent(
    string Subject,
    string Html,
    IReadOnlyList<TemplateIssue> Warnings);

/// <summary>
/// The save-time gate every write path goes through: repair WYSIWYG damage, then refuse content
/// Scriban cannot parse. Runs in the endpoint body rather than as a FluentValidation rule because
/// <c>ValidationFilter</c> emits RFC7807, and the admin panel needs the per-issue line/column
/// payload to point the author at the broken line.
/// </summary>
internal static class TemplateContentValidator
{
    public static bool TryPrepare(
        string? subject,
        string? html,
        string? context,
        ITemplateContextResolver contextResolver,
        out PreparedTemplateContent prepared,
        out IResult? error)
    {
        string normalizedSubject = ScribanContentNormalizer.Normalize(subject);
        string normalizedHtml = ScribanContentNormalizer.Normalize(html);

        TemplateContextDefinition? definition = contextResolver.GetDefinition(context);
        string[]? knownVariables = definition?.Placeholders.Select(p => p.Name).ToArray();

        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(normalizedSubject, normalizedHtml, knownVariables);

        if (analysis.HasErrors)
        {
            prepared = null!;
            error = Results.BadRequest(new TemplateValidationResponse(
                "Şablonda sözdizimi hatası var.",
                ScribanTemplateAnalyzer.SyntaxErrorKind,
                analysis.Errors,
                analysis.Warnings));
            return false;
        }

        prepared = new PreparedTemplateContent(normalizedSubject, normalizedHtml, analysis.Warnings);
        error = null;
        return true;
    }
}
