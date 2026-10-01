using Kariyer.Mail.Api.Common.Configuration;
using Kariyer.Mail.Api.Features.Recruiting.ApplicationStageChanged;
using Xunit;

namespace Kariyer.Mail.Api.UnitTests;

public class StageMailPolicyTests
{
    [Theory]
    [InlineData("INTERVIEW", "OFFER", nameof(EmailTemplateSettings.ApplicationOfferTemplateSlug))]
    [InlineData("OFFER", "HIRED", nameof(EmailTemplateSettings.ApplicationHiredTemplateSlug))]
    [InlineData("NEW", "REJECTED", nameof(EmailTemplateSettings.ApplicationRejectedTemplateSlug))]
    [InlineData("INTERVIEW", "REJECTED", nameof(EmailTemplateSettings.ApplicationRejectedTemplateSlug))]
    public void Decisions_the_candidate_hears_about(string from, string to, string expected) =>
        Assert.Equal(expected, StageMailPolicy.SettingsKeyFor(from, to));

    [Theory]
    [InlineData("NEW", "REVIEWING")]
    [InlineData("REVIEWING", "CONTACT")]
    [InlineData("OFFER", "HOLD")]
    [InlineData("HOLD", "NEW")]
    public void Housekeeping_is_silent(string from, string to) =>
        Assert.Null(StageMailPolicy.SettingsKeyFor(from, to));

    [Fact]
    public void INTERVIEW_is_announced_by_the_invitation_not_here() =>
        Assert.Null(StageMailPolicy.SettingsKeyFor("REVIEWING", "INTERVIEW"));

    [Theory]
    [InlineData("HIRED", "OFFER")]
    [InlineData("HIRED", "HOLD")]
    [InlineData("REJECTED", "HOLD")]
    public void Correcting_a_final_decision_is_silent(string from, string to)
    {
        // The candidate may already hold a letter saying the opposite; a second one that
        // contradicts it is worse than none. Within the hold, this move is also what cancels
        // the first letter before it goes out.
        Assert.Null(StageMailPolicy.SettingsKeyFor(from, to));
    }

    [Fact]
    public void Turning_a_hire_into_a_rejection_is_a_new_decision() =>
        Assert.Equal(
            nameof(EmailTemplateSettings.ApplicationRejectedTemplateSlug),
            StageMailPolicy.SettingsKeyFor("HIRED", "REJECTED"));

    [Fact]
    public void A_no_op_move_is_silent() =>
        Assert.Null(StageMailPolicy.SettingsKeyFor("OFFER", "OFFER"));

    [Theory]
    [InlineData("INTERVIEW", true)]
    [InlineData("OFFER", true)]
    [InlineData("HIRED", true)]
    [InlineData("NEW", false)]
    [InlineData("REVIEWING", false)]
    [InlineData("HOLD", false)]
    public void Knows_whether_a_rejection_came_after_meeting_the_company(string from, bool expected) =>
        Assert.Equal(expected, StageMailPolicy.IsAfterInterview(from));
}
