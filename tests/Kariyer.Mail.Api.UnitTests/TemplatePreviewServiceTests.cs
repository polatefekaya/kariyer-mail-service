using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.Templates;
using Kariyer.Mail.Api.Features.Templates.PreviewTemplate;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kariyer.Mail.Api.UnitTests;

public class TemplatePreviewServiceTests
{
    private static TemplatePreviewService BuildService() =>
        new(new TemplateContextResolver(
            Options.Create(new EmailTemplateSettings()),
            NullLogger<TemplateContextResolver>.Instance));

    [Fact]
    public async Task Renders_an_automated_template_against_its_own_vocabulary()
    {
        // The reported bug: this used to 400 with "missing variable" because the browser sent bulk
        // dummy data, and the UI displayed that as a syntax error.
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: "Profilini tamamla {{ FullName }}",
            html: "<p>{{ ReminderStep }}. hatırlatma — {{ AccountType }}</p>",
            context: "AccountDidNotCompleted.Step1",
            overrides: null,
            ct: CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.UnknownVariables);
        Assert.Contains("Ahmet Yılmaz", result.RenderedSubject);
        Assert.Contains("2. hatırlatma", result.RenderedHtml);
    }

    [Fact]
    public async Task Reports_unknown_variables_without_failing_the_render()
    {
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: "Merhaba",
            html: "<p>{{ FullName }} — {{ ActionUrl }}</p>",
            context: "AccountCompleted",
            overrides: null,
            ct: CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(["ActionUrl"], result.UnknownVariables);
        Assert.Contains("Ahmet Yılmaz", result.RenderedHtml);   // still rendered
        Assert.Contains("FullName", result.UsedVariables);
    }

    [Fact]
    public async Task Collects_every_unknown_variable_in_one_pass()
    {
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: string.Empty,
            html: "{{ Foo }}{{ Bar }}{{ Baz }}",
            context: "AccountCompleted",
            overrides: null,
            ct: CancellationToken.None);

        // Strict mode aborted on the first miss; the author only ever saw one problem at a time.
        Assert.Equal(["Bar", "Baz", "Foo"], result.UnknownVariables);
    }

    [Fact]
    public async Task Repairs_wysiwyg_encoded_delimiters_before_rendering()
    {
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: string.Empty,
            html: "<p>{{&nbsp;FullName&nbsp;}}</p>",
            context: "AccountCompleted",
            overrides: null,
            ct: CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("Ahmet Yılmaz", result.RenderedHtml);
    }

    [Fact]
    public async Task Preserves_html_entities_outside_scriban_spans()
    {
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: string.Empty,
            html: "<p>Ar&amp;Ge — {{ FullName }}</p>",
            context: "AccountCompleted",
            overrides: null,
            ct: CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("Ar&amp;Ge", result.RenderedHtml);
    }

    [Fact]
    public async Task Fails_on_a_genuine_syntax_error()
    {
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: string.Empty,
            html: "{{ for x in }}",
            context: "AccountCompleted",
            overrides: null,
            ct: CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(TemplatePreviewErrorKind.SyntaxError, result.ErrorKind);
        Assert.NotEmpty(result.Issues);
    }

    [Fact]
    public async Task Rejects_an_unknown_context()
    {
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: "x", html: "y", context: "NotARealContext", overrides: null, ct: CancellationToken.None);

        Assert.Equal(TemplatePreviewErrorKind.UnknownContext, result.ErrorKind);
    }

    [Fact]
    public async Task Rejects_empty_content()
    {
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: null, html: null, context: null, overrides: null, ct: CancellationToken.None);

        Assert.Equal(TemplatePreviewErrorKind.EmptyContent, result.ErrorKind);
    }

    [Fact]
    public async Task Client_overrides_win_over_the_seeded_examples()
    {
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: "{{ FullName }}",
            html: "<p>x</p>",
            context: "AccountCompleted",
            overrides: new Dictionary<string, object?> { ["FullName"] = "Polat Efe" },
            ct: CancellationToken.None);

        Assert.Equal("Polat Efe", result.RenderedSubject);
    }

    [Fact]
    public async Task Defaults_to_the_bulk_vocabulary_when_no_context_is_given()
    {
        TemplatePreviewResult result = await BuildService().RenderAsync(
            subject: string.Empty,
            html: "<p>{{ FirstName }} {{ LastName }}</p>",
            context: null,
            overrides: null,
            ct: CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(TemplateContextRegistry.BulkEmailContext, result.Context);
        Assert.Empty(result.UnknownVariables);
    }
}
