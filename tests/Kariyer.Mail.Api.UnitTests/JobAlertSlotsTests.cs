using Kariyer.Mail.Api.Features.JobAlert;
using Xunit;

namespace Kariyer.Mail.Api.UnitTests;

/// <summary>
/// The slot decides which of the three templates a candidate receives. Every rule here
/// fails silently if it breaks — the wrong wording, or no mail at all — so each one is
/// pinned.
/// </summary>
public class JobAlertSlotsTests
{
    [Theory]
    [InlineData("morning", "morning")]
    [InlineData("noon", "noon")]
    [InlineData("evening", "evening")]
    public void Recognises_each_window(string input, string expected)
    {
        string slot = JobAlertSlots.Resolve(input, out bool recognised);

        Assert.Equal(expected, slot);
        Assert.True(recognised);
    }

    [Theory]
    [InlineData("MORNING")]
    [InlineData("  Evening  ")]
    [InlineData("Noon")]
    public void Is_forgiving_about_casing_and_whitespace(string input)
    {
        JobAlertSlots.Resolve(input, out bool recognised);

        // The publisher is a different language and a different repo; a casing difference
        // must not cost somebody their digest.
        Assert.True(recognised);
    }

    [Fact]
    public void Falls_back_to_morning_for_an_unknown_slot_rather_than_failing()
    {
        // A slot this service has not been taught yet means the publisher shipped first.
        // That is a deployment-ordering problem, and dropping the mail would be worse.
        string slot = JobAlertSlots.Resolve("midnight", out bool recognised);

        Assert.Equal(JobAlertSlots.Morning, slot);
        Assert.False(recognised);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Treats_an_absent_slot_as_morning_without_complaining(string? input)
    {
        string slot = JobAlertSlots.Resolve(input, out bool recognised);

        Assert.Equal(JobAlertSlots.Morning, slot);
        // Expected, not anomalous — no warning should be logged for it.
        Assert.True(recognised);
    }

    [Fact]
    public void Treats_always_as_morning_without_complaining()
    {
        // The publisher sends "always" when every send window is disabled. Warning on it
        // would log on every single message in that configuration.
        string slot = JobAlertSlots.Resolve(JobAlertSlots.Always, out bool recognised);

        Assert.Equal(JobAlertSlots.Morning, slot);
        Assert.True(recognised);
    }

    [Fact]
    public void Never_returns_a_slot_the_slug_switch_cannot_map()
    {
        // SlugFor has three arms. Anything else silently takes the morning branch, so
        // Resolve must never emit a fourth value.
        string[] permitted = [JobAlertSlots.Morning, JobAlertSlots.Noon, JobAlertSlots.Evening];

        foreach (string? input in new[] { null, "", "morning", "noon", "evening", "always", "nonsense", "NOON " })
        {
            Assert.Contains(JobAlertSlots.Resolve(input, out _), permitted);
        }
    }
}
