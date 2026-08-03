using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Kariyer.Mail.Api.Common.Templating;

/// <summary>
/// Repairs Scriban delimiters mangled by the admin panel's WYSIWYG editor.
///
/// TinyMCE treats template markup as prose: it HTML-encodes <c>&gt;</c> and <c>&amp;</c>, turns
/// runs of spaces into <c>&amp;nbsp;</c>, and happily wraps part of an expression in a
/// <c>&lt;span&gt;</c> or <c>&lt;strong&gt;</c> when the author formats across a placeholder. Any
/// of those turn <c>{{ FullName }}</c> into something Scriban refuses to parse.
///
/// The repair is deliberately scoped to the inside of Scriban delimiter spans. The preview endpoint
/// used to run <see cref="WebUtility.HtmlDecode"/> over the whole body, which "fixed" the preview
/// while corrupting every legitimate <c>&amp;amp;</c> and <c>&amp;lt;</c> in the surrounding email
/// HTML — and, because create/update did not do the same thing, the stored template stayed broken
/// even though the preview looked right.
/// </summary>
public static partial class ScribanContentNormalizer
{
    // How many decode passes before we give up chasing double-encoded entities. Iterating to a
    // fixpoint (rather than decoding once) is what makes Normalize idempotent.
    private const int MaxDecodePasses = 3;

    /// <summary>Statement, expression and raw blocks: {%{ ... }%}, {{~ ... ~}}, {{ ... }}.</summary>
    [GeneratedRegex(@"\{%\{.*?\}%\}|\{\{~?.*?~?\}\}", RegexOptions.Singleline)]
    private static partial Regex ScribanSpanRegex { get; }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex { get; }

    /// <summary>Entity-encoded braces, for editors that escape the delimiters themselves.</summary>
    [GeneratedRegex(@"&#0*123;|&lbrace;", RegexOptions.IgnoreCase)]
    private static partial Regex EncodedOpenBraceRegex { get; }

    [GeneratedRegex(@"&#0*125;|&rbrace;", RegexOptions.IgnoreCase)]
    private static partial Regex EncodedCloseBraceRegex { get; }

    /// <summary>Returns <paramref name="content"/> with Scriban spans repaired. Idempotent.</summary>
    public static string Normalize(string? content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;

        string working = RestoreEncodedDelimiters(content);

        return ScribanSpanRegex.Replace(working, static match => NormalizeSpan(match.Value));
    }

    /// <summary>True when <see cref="Normalize"/> would change the content. Used by the backfill.</summary>
    public static bool NeedsNormalization(string? content) =>
        !string.IsNullOrEmpty(content) && !string.Equals(Normalize(content), content, StringComparison.Ordinal);

    /// <summary>
    /// If the delimiters themselves were entity-encoded there is no span to find, so unescape the
    /// braces first. Only done when the content has no usable delimiters left — otherwise a literal
    /// <c>&amp;#123;</c> in the email body would be rewritten into a brace it never was.
    /// </summary>
    private static string RestoreEncodedDelimiters(string content)
    {
        if (content.Contains("{{", StringComparison.Ordinal)) return content;
        if (!EncodedOpenBraceRegex.IsMatch(content)) return content;

        return EncodedCloseBraceRegex.Replace(EncodedOpenBraceRegex.Replace(content, "{"), "}");
    }

    private static string NormalizeSpan(string span)
    {
        // Strip editor-injected markup first so a decode cannot resurrect it as text.
        string cleaned = HtmlTagRegex.Replace(span, string.Empty);

        for (int pass = 0; pass < MaxDecodePasses; pass++)
        {
            string decoded = WebUtility.HtmlDecode(cleaned);
            if (string.Equals(decoded, cleaned, StringComparison.Ordinal)) break;
            cleaned = decoded;
        }

        return StripInvisibleWhitespace(cleaned);
    }

    /// <summary>
    /// Non-breaking and zero-width characters are invisible in the editor but are not whitespace to
    /// Scriban's lexer — <c>{{ FullName }}</c> fails to parse.
    /// </summary>
    private static string StripInvisibleWhitespace(string value)
    {
        StringBuilder? builder = null;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool isNbsp = c is '\u00A0' or '\u202F' or '\u2007';
            bool isZeroWidth = c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF';

            if (!isNbsp && !isZeroWidth)
            {
                builder?.Append(c);
                continue;
            }

            builder ??= new StringBuilder(value.Length).Append(value, 0, i);
            if (isNbsp) builder.Append(' ');
        }

        return builder?.ToString() ?? value;
    }
}
