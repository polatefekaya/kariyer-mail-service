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
        Assert.Equal(29, TemplateContextRegistry.SystemSlots.Count);
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

    [Theory]
    [InlineData("JobAlertMorning")]
    [InlineData("JobAlertNoon")]
    [InlineData("JobAlertEvening")]
    public void JobAlert_slots_match_what_the_consumer_supplies(string context)
    {
        // Same reasoning as ServiceLead: nothing else notices if this vocabulary drifts from
        // the templateData JobAlertReadyConsumer builds — the editor would simply offer
        // variables that render empty, in the only emails that go to people who opted in.
        Assert.True(TemplateContextRegistry.TryGetByContext(context, out TemplateContextDefinition definition));

        string[] names = definition.Placeholders.Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            new[] { "AlertUrl", "FullName", "JobCount", "UnsubscribeUrl" },
            names);
    }

    [Theory]
    [InlineData("JobAlertMorning")]
    [InlineData("JobAlertNoon")]
    [InlineData("JobAlertEvening")]
    public void JobAlert_slots_offer_an_unsubscribe_link(string context)
    {
        // These are the only standing subscriptions the service sends. A template authored
        // without an unsubscribe link would be a compliance problem, so the variable has to
        // be in the vocabulary the editor offers — in every window, not just one.
        Assert.True(TemplateContextRegistry.TryGetByContext(context, out TemplateContextDefinition definition));
        Assert.Contains(definition.Placeholders, p => p.Name == "UnsubscribeUrl");
    }

    [Fact]
    public void JobAlert_windows_share_one_vocabulary()
    {
        // The windows exist so the WORDING can differ; the data does not. Three hand-copied
        // lists would drift, and the drift would be invisible until an editor picked a
        // variable that renders empty in one window only.
        string[][] vocabularies = new[] { "JobAlertMorning", "JobAlertNoon", "JobAlertEvening" }
            .Select(context =>
            {
                TemplateContextRegistry.TryGetByContext(context, out TemplateContextDefinition d);
                return d.Placeholders.Select(p => p.Name).OrderBy(n => n).ToArray();
            })
            .ToArray();

        Assert.Equal(vocabularies[0], vocabularies[1]);
        Assert.Equal(vocabularies[0], vocabularies[2]);
    }

    [Theory]
    [InlineData("InterviewInvited")]
    [InlineData("InterviewRescheduled")]
    public void Interview_answer_slots_offer_both_links(string context)
    {
        // The candidate answers an invitation from the e-mail, with no session: these signed
        // links ARE the authorisation. A template authored without them leaves the answer
        // unreachable, and confirmation_status stuck on PENDING forever.
        Assert.True(TemplateContextRegistry.TryGetByContext(context, out TemplateContextDefinition definition));

        Assert.Contains(definition.Placeholders, p => p.Name == "AcceptUrl");
        Assert.Contains(definition.Placeholders, p => p.Name == "DeclineUrl");
    }

    [Fact]
    public void Cancellation_offers_no_answer_links()
    {
        // There is nothing left to answer, and a link that still worked would let someone
        // confirm a meeting the company has already called off.
        Assert.True(TemplateContextRegistry.TryGetByContext("InterviewCancelled", out TemplateContextDefinition definition));

        Assert.DoesNotContain(definition.Placeholders, p => p.Name is "AcceptUrl" or "DeclineUrl");
    }

    [Fact]
    public void Interview_slots_match_what_their_consumers_supply()
    {
        // Same reasoning as ServiceLead: keep these identical to the templateData dictionaries
        // in the three consumers under Features/Recruiting.
        string[] shared =
        [
            "CandidateName", "CompanyName", "Duration", "InterviewDateTime", "InterviewType",
            "JobTitle", "Location", "LocationLabel", "Message", "TimeZone",
        ];

        Assert.Equal(
            [.. shared.Concat(["AcceptUrl", "DeclineUrl", "InvitedByName"]).OrderBy(n => n)],
            Vocabulary("InterviewInvited"));

        Assert.Equal(
            [.. shared.Concat(["AcceptUrl", "ChangedByName", "DeclineUrl", "PreviousDateTime"]).OrderBy(n => n)],
            Vocabulary("InterviewRescheduled"));

        Assert.Equal(
            ["CancelledByName", "CandidateName", "CompanyName", "InterviewDateTime", "JobTitle", "Message", "TimeZone"],
            Vocabulary("InterviewCancelled"));
    }

    [Fact]
    public void Application_slots_match_what_their_consumers_supply()
    {
        // Same reasoning as ServiceLead: keep these identical to the templateData dictionaries
        // the consumers under Features/Recruiting build — and, for the three decisions, to what
        // ApplicationStageChangedConsumer stores on the held row.
        Assert.Equal(
            ["ApplicationsUrl", "CandidateName", "CompanyName", "JobTitle", "SubmittedAt"],
            Vocabulary("ApplicationSubmitted"));

        Assert.Equal(
            ["CandidateName", "CompanyName", "IsQuickApply", "JobTitle", "ReviewUrl", "SubmittedAt"],
            Vocabulary("ApplicationSubmitted.Company"));

        Assert.Equal(
            ["CandidateName", "CompanyName", "JobTitle", "ReviewUrl", "WithdrawnAt"],
            Vocabulary("ApplicationWithdrawn"));

        Assert.Equal(["CandidateName", "CompanyName", "JobTitle"], Vocabulary("ApplicationStage.Offer"));
        Assert.Equal(["CandidateName", "CompanyName", "JobTitle"], Vocabulary("ApplicationStage.Hired"));
        Assert.Equal(
            ["AfterInterview", "CandidateName", "CompanyName", "JobTitle"],
            Vocabulary("ApplicationStage.Rejected"));
    }

    [Theory]
    [InlineData("InterviewAnswered.Accepted")]
    [InlineData("InterviewAnswered.Declined")]
    public void Interview_answer_slots_match_what_their_consumer_supplies(string context)
    {
        Assert.Equal(
            [
                "CandidateName", "CompanyName", "InterviewDateTime", "InterviewType", "JobTitle",
                "RecipientName", "ReviewUrl", "TimeZone",
            ],
            Vocabulary(context));
    }

    [Theory]
    [InlineData("InterviewAnswered.Accepted")]
    [InlineData("InterviewAnswered.Declined")]
    public void Interview_answer_slots_never_offer_the_candidates_links(string context)
    {
        // These go to the company side. The accept/decline links answer for whoever opens them,
        // so a template here must not be able to hand a recruiter the candidate's answer.
        Assert.DoesNotContain(Vocabulary(context), name => name is "AcceptUrl" or "DeclineUrl");
    }

    private static string[] Vocabulary(string context)
    {
        Assert.True(TemplateContextRegistry.TryGetByContext(context, out TemplateContextDefinition definition));

        return [.. definition.Placeholders.Select(p => p.Name).OrderBy(n => n)];
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
