using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Kariyer.Mail.Api.Common.Templating;

/// <summary>One problem found in a template. <see cref="Line"/>/<see cref="Column"/> are 1-based.</summary>
public sealed record TemplateIssue(string Field, string Kind, string Message, int Line, int Column);

public sealed record TemplateAnalysis(
    IReadOnlyList<TemplateIssue> Errors,
    IReadOnlyList<TemplateIssue> Warnings,
    IReadOnlySet<string> ReferencedVariables)
{
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Static analysis for a template's subject and body: real Scriban syntax errors (which must block
/// a save) and references to variables the template's context will never supply (which must not —
/// an author may legitimately be mid-edit, and the send path renders missing variables as empty).
///
/// Nothing validated templates on save before this, so a body mangled by the WYSIWYG editor was
/// happily persisted and only blew up hours later inside the dispatch consumer.
/// </summary>
public static class ScribanTemplateAnalyzer
{
    public const string SubjectField = "SubjectTemplate";
    public const string BodyField = "HtmlContent";

    public const string SyntaxErrorKind = "SyntaxError";
    public const string UnknownVariableKind = "UnknownVariable";

    /// <summary>Scriban's own globals (string, date, math, …). Never reported as unknown.</summary>
    private static readonly HashSet<string> BuiltinNames =
        new(new TemplateContext().BuiltinObject.Keys, StringComparer.Ordinal);

    /// <param name="knownVariables">
    /// The context's vocabulary. Pass null to skip unknown-variable analysis entirely.
    /// </param>
    public static TemplateAnalysis Analyze(string? subject, string? html, IReadOnlyCollection<string>? knownVariables)
    {
        List<TemplateIssue> errors = [];
        HashSet<string> referenced = new(StringComparer.Ordinal);

        AnalyzeField(SubjectField, subject, errors, referenced);
        AnalyzeField(BodyField, html, errors, referenced);

        List<TemplateIssue> warnings = [];

        if (knownVariables is not null && errors.Count == 0)
        {
            HashSet<string> known = new(knownVariables, StringComparer.Ordinal);

            foreach (string name in referenced.Where(n => !known.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
            {
                warnings.Add(new TemplateIssue(
                    BodyField,
                    UnknownVariableKind,
                    $"'{{{{ {name} }}}}' bu şablon bağlamında tanımlı değil; gönderimde boş görünecek.",
                    0, 0));
            }
        }

        return new TemplateAnalysis(errors, warnings, referenced);
    }

    private static void AnalyzeField(string field, string? text, List<TemplateIssue> errors, HashSet<string> referenced)
    {
        if (string.IsNullOrEmpty(text)) return;

        Template template = Template.Parse(text, sourceFilePath: field);

        if (template.HasErrors)
        {
            foreach (LogMessage message in template.Messages)
            {
                if (message.Type != ParserMessageType.Error) continue;

                errors.Add(new TemplateIssue(
                    field,
                    SyntaxErrorKind,
                    message.Message,
                    message.Span.Start.Line + 1,      // Scriban positions are 0-based
                    message.Span.Start.Column + 1));
            }

            // A parse failure means the tree is unusable; don't try to read variables out of it.
            return;
        }

        if (template.Page is not null) CollectVariables(template.Page, referenced, []);
    }

    /// <summary>
    /// Walks the syntax tree collecting global variable reads.
    ///
    /// Two things are deliberately not reported: the member half of <c>a.b</c> (only <c>a</c> is a
    /// variable — <c>b</c> is a lookup on it), and names bound by the template itself, e.g. the
    /// loop variable in <c>{{ for item in items }}</c>.
    /// </summary>
    private static void CollectVariables(ScriptNode node, HashSet<string> referenced, HashSet<string> bound)
    {
        switch (node)
        {
            case ScriptVariableGlobal variable:
                if (!bound.Contains(variable.Name) && !BuiltinNames.Contains(variable.Name))
                    referenced.Add(variable.Name);
                return;

            case ScriptMemberExpression member:
                // Only the target is a variable reference; Member is a property name on it.
                if (member.Target is not null) CollectVariables(member.Target, referenced, bound);
                return;

            case ScriptForStatement forStatement:
                if (forStatement.Variable is ScriptVariable loopVariable)
                    bound = new HashSet<string>(bound, StringComparer.Ordinal) { loopVariable.Name };
                break;

            case ScriptCaptureStatement capture:
                if (capture.Target is ScriptVariable captured)
                    bound = new HashSet<string>(bound, StringComparer.Ordinal) { captured.Name };
                break;

            case ScriptAssignExpression assign:
                if (assign.Target is ScriptVariable assigned)
                    bound = new HashSet<string>(bound, StringComparer.Ordinal) { assigned.Name };
                break;
        }

        int count = node.ChildrenCount;
        for (int i = 0; i < count; i++)
        {
            if (node.GetChildren(i) is { } child) CollectVariables(child, referenced, bound);
        }
    }
}
