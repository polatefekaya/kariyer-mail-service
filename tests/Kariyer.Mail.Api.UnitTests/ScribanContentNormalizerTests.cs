using Kariyer.Mail.Api.Common.Templating;
using Xunit;

namespace Kariyer.Mail.Api.UnitTests;

public class ScribanContentNormalizerTests
{
    [Fact]
    public void Decodes_entities_inside_a_scriban_span()
    {
        Assert.Equal("{{ if count > 5 }}", ScribanContentNormalizer.Normalize("{{ if count &gt; 5 }}"));
    }

    [Fact]
    public void Leaves_entities_outside_a_scriban_span_untouched()
    {
        // The old blanket HtmlDecode in the preview endpoint corrupted exactly this.
        const string html = "<p>Ar&amp;Ge &lt;b&gt;etiketi&lt;/b&gt; &nbsp; korunmalı</p>";
        Assert.Equal(html, ScribanContentNormalizer.Normalize(html));
    }

    [Fact]
    public void Replaces_non_breaking_spaces_inside_a_span()
    {
        Assert.Equal("{{ FullName }}", ScribanContentNormalizer.Normalize("{{&nbsp;FullName&nbsp;}}"));
    }

    [Fact]
    public void Strips_editor_injected_markup_from_inside_a_span()
    {
        Assert.Equal("{{ FullName }}", ScribanContentNormalizer.Normalize("{{ <strong>FullName</strong> }}"));
    }

    [Fact]
    public void Handles_whitespace_stripping_delimiters()
    {
        Assert.Equal("{{~ FullName ~}}", ScribanContentNormalizer.Normalize("{{~&nbsp;FullName&nbsp;~}}"));
    }

    [Fact]
    public void Handles_raw_blocks()
    {
        Assert.Equal("{%{ a > b }%}", ScribanContentNormalizer.Normalize("{%{ a &gt; b }%}"));
    }

    [Fact]
    public void Restores_entity_encoded_delimiters_when_no_real_ones_remain()
    {
        Assert.Equal("{{ FullName }}", ScribanContentNormalizer.Normalize("&#123;&#123; FullName &#125;&#125;"));
    }

    [Fact]
    public void Does_not_touch_encoded_braces_when_real_delimiters_exist()
    {
        const string mixed = "{{ FullName }} literal &#123;&#123;";
        Assert.Equal(mixed, ScribanContentNormalizer.Normalize(mixed));
    }

    [Theory]
    [InlineData("{{ if count &gt; 5 }}")]
    [InlineData("{{&nbsp;FullName&nbsp;}}")]
    [InlineData("{{ <span>FullName</span> }} &amp; more")]
    [InlineData("<p>Nothing to do here</p>")]
    public void Is_idempotent(string input)
    {
        string once = ScribanContentNormalizer.Normalize(input);
        Assert.Equal(once, ScribanContentNormalizer.Normalize(once));
    }

    [Fact]
    public void Handles_multiple_spans_in_one_document()
    {
        const string input = "<p>{{&nbsp;FirstName&nbsp;}} &amp; {{ if age &gt; 18 }}yetişkin{{ end }}</p>";
        const string expected = "<p>{{ FirstName }} &amp; {{ if age > 18 }}yetişkin{{ end }}</p>";

        Assert.Equal(expected, ScribanContentNormalizer.Normalize(input));
    }

    [Fact]
    public void NeedsNormalization_reports_only_actual_damage()
    {
        Assert.True(ScribanContentNormalizer.NeedsNormalization("{{&nbsp;FullName&nbsp;}}"));
        Assert.False(ScribanContentNormalizer.NeedsNormalization("{{ FullName }}"));
        Assert.False(ScribanContentNormalizer.NeedsNormalization("<p>A &amp; B</p>"));
        Assert.False(ScribanContentNormalizer.NeedsNormalization(null));
    }

    [Fact]
    public void Normalized_output_actually_parses()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
            subject: string.Empty,
            html: ScribanContentNormalizer.Normalize("{{&nbsp;if&nbsp;count&nbsp;&gt;&nbsp;5&nbsp;}}çok{{ end }}"),
            knownVariables: null);

        Assert.False(analysis.HasErrors);
    }
}
