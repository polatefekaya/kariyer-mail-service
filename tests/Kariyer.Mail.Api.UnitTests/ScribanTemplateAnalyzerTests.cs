using Kariyer.Mail.Api.Common.Templating;
using Xunit;

namespace Kariyer.Mail.Api.UnitTests;

public class ScribanTemplateAnalyzerTests
{
    [Fact]
    public void Reports_no_errors_for_a_valid_template()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
            "Merhaba {{ FullName }}", "<p>{{ FullName }}</p>", knownVariables: null);

        Assert.False(analysis.HasErrors);
        Assert.Contains("FullName", analysis.ReferencedVariables);
    }

    [Fact]
    public void Reports_a_syntax_error_with_field_and_position()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
            subject: string.Empty, html: "<p>\n{{ for x in }}\n</p>", knownVariables: null);

        Assert.True(analysis.HasErrors);

        TemplateIssue issue = analysis.Errors[0];
        Assert.Equal(ScribanTemplateAnalyzer.BodyField, issue.Field);
        Assert.Equal(ScribanTemplateAnalyzer.SyntaxErrorKind, issue.Kind);
        Assert.True(issue.Line >= 1, "Line numbers are reported 1-based for display.");
        Assert.True(issue.Column >= 1, "Column numbers are reported 1-based for display.");
    }

    [Fact]
    public void Attributes_a_subject_error_to_the_subject_field()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
            subject: "{{ if }}", html: "<p>ok</p>", knownVariables: null);

        Assert.True(analysis.HasErrors);
        Assert.Contains(analysis.Errors, e => e.Field == ScribanTemplateAnalyzer.SubjectField);
    }

    [Fact]
    public void Does_not_report_loop_variables_as_references()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
            subject: string.Empty,
            html: "{{ for item in Items }}{{ item }}{{ end }}",
            knownVariables: null);

        Assert.Contains("Items", analysis.ReferencedVariables);
        Assert.DoesNotContain("item", analysis.ReferencedVariables);
    }

    [Fact]
    public void Does_not_report_builtins_as_references()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
            subject: string.Empty, html: "{{ string.upcase FullName }}", knownVariables: null);

        Assert.Contains("FullName", analysis.ReferencedVariables);
        Assert.DoesNotContain("string", analysis.ReferencedVariables);
    }

    [Fact]
    public void Reports_only_the_root_of_a_member_access()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
            subject: string.Empty, html: "{{ Company.Name }}", knownVariables: null);

        Assert.Contains("Company", analysis.ReferencedVariables);
        Assert.DoesNotContain("Name", analysis.ReferencedVariables);
    }

    [Fact]
    public void Warns_about_variables_the_context_does_not_supply()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
            subject: string.Empty,
            html: "{{ FullName }} {{ ActionUrl }}",
            knownVariables: ["FullName", "AccountType"]);

        Assert.False(analysis.HasErrors);
        Assert.Single(analysis.Warnings);
        Assert.Equal(ScribanTemplateAnalyzer.UnknownVariableKind, analysis.Warnings[0].Kind);
        Assert.Contains("ActionUrl", analysis.Warnings[0].Message);
    }

    [Fact]
    public void Skips_unknown_variable_analysis_when_no_vocabulary_is_given()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(
            subject: string.Empty, html: "{{ Whatever }}", knownVariables: null);

        Assert.Empty(analysis.Warnings);
    }

    [Fact]
    public void Ignores_empty_fields()
    {
        TemplateAnalysis analysis = ScribanTemplateAnalyzer.Analyze(null, null, knownVariables: null);

        Assert.False(analysis.HasErrors);
        Assert.Empty(analysis.ReferencedVariables);
    }
}
