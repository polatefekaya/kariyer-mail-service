using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kariyer.Mail.Api.UnitTests;

public class TemplateContextRegistryTests
{
    private static TemplateContextResolver BuildResolver(EmailTemplateSettings settings) =>
        new(Options.Create(settings), NullLogger<TemplateContextResolver>.Instance);

    [Fact]
    public void Declares_every_system_slot()
    {
        Assert.Equal(15, TemplateContextRegistry.SystemSlots.Count);
    }

    [Fact]
    public void ServiceLead_slot_matches_what_the_endpoint_supplies()
    {
        // ServiceLead is the only slot fed by a public HTTP endpoint rather than a bus event,
        // so nothing else in the system would notice if its vocabulary drifted from the
        // templateData SubmitLeadEndpoint builds — the editor would just offer variables that
        // render empty. Keep this list identical to that dictionary.
        Assert.True(TemplateContextRegistry.TryGetByContext("ServiceLead", out TemplateContextDefinition definition));

        string[] names = definition.Placeholders.Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            new[]
            {
                "CompanyName", "Email", "FullName", "Locale", "Message",
                "PageLabel", "PagePath", "Phone", "SubmittedAt",
            },
            names);
    }

    [Fact]
    public void Registry_and_settings_never_drift()
    {
        // Same assertion the app runs at startup.
        TemplateContextResolver.AssertRegistryMatchesSettings();
    }

    [Fact]
    public void Every_context_has_a_vocabulary()
    {
        Assert.All(TemplateContextRegistry.All, d => Assert.NotEmpty(d.Placeholders));
    }

    [Theory]
    [InlineData("AccountDidNotCompleted.Step1")]
    [InlineData("AccountDidNotCompleted.Step2")]
    [InlineData("AccountDidNotCompleted.Step3")]
    public void Reminder_slots_have_the_vocabulary_their_consumer_supplies(string context)
    {
        // These three slots had no placeholder set at all, which is what made the editor fall back
        // to the bulk-email variables and report a syntax error on a perfectly valid template.
        Assert.True(TemplateContextRegistry.TryGetByContext(context, out TemplateContextDefinition definition));

        string[] names = definition.Placeholders.Select(p => p.Name).ToArray();
        Assert.Contains("FullName", names);
        Assert.Contains("AccountType", names);
        Assert.Contains("ReminderStep", names);
    }

    [Fact]
    public void Bulk_context_does_not_advertise_variables_nothing_supplies()
    {
        TemplateContextDefinition bulk = TemplateContextRegistry.BulkEmail;
        string[] names = bulk.Placeholders.Select(p => p.Name).ToArray();

        Assert.DoesNotContain("ActionUrl", names);
        Assert.Contains("Email", names);
        Assert.Contains("FirstName", names);
        Assert.Contains("Province", names);   // real resolver key that used to be undocumented
    }

    [Fact]
    public void Placeholders_render_their_scriban_syntax()
    {
        Assert.Equal("{{ FullName }}", new TemplatePlaceholder("FullName", "Ahmet", "Ad").ScribanSyntax);
    }

    [Fact]
    public void Resolves_context_from_a_configured_slug()
    {
        TemplateContextResolver resolver = BuildResolver(new EmailTemplateSettings
        {
            AccountCreatedTemplateSlug = "account-created"
        });

        Assert.Equal("AccountCreated", resolver.ResolveContext("account-created"));
    }

    [Fact]
    public void Falls_back_to_bulk_for_unslugged_and_unknown_templates()
    {
        TemplateContextResolver resolver = BuildResolver(new EmailTemplateSettings());

        Assert.Equal(TemplateContextRegistry.BulkEmailContext, resolver.ResolveContext(null));
        Assert.Equal(TemplateContextRegistry.BulkEmailContext, resolver.ResolveContext("not-a-slot"));
    }

    [Fact]
    public void Resolves_slug_from_a_settings_key()
    {
        TemplateContextResolver resolver = BuildResolver(new EmailTemplateSettings
        {
            AccountFrozenTemplateSlug = "account-frozen"
        });

        Assert.Equal("account-frozen", resolver.ResolveSlug(nameof(EmailTemplateSettings.AccountFrozenTemplateSlug)));
        Assert.Null(resolver.ResolveSlug("NotASettingsKey"));
    }

    [Fact]
    public void Example_data_covers_the_whole_vocabulary()
    {
        TemplateContextResolver resolver = BuildResolver(new EmailTemplateSettings());

        IReadOnlyDictionary<string, object?> examples = resolver.GetExampleData("AccountApproved");

        Assert.Equal(["ApprovedAt", "FullName"], examples.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void Unknown_context_example_data_falls_back_to_bulk()
    {
        TemplateContextResolver resolver = BuildResolver(new EmailTemplateSettings());

        Assert.Contains("FirstName", resolver.GetExampleData("nope").Keys);
    }
}
