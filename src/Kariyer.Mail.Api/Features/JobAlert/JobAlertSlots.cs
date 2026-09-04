namespace Kariyer.Mail.Api.Features.JobAlert;

/// <summary>
/// Which of the three send windows a digest belongs to, and therefore which template it
/// renders.
///
/// Pure and separate from the consumer on purpose. The fallback below is the kind of rule
/// that fails silently — an unrecognised slot would quietly send the wrong wording, or
/// nothing at all — and it cannot be exercised while it lives inside a consumer that needs
/// a DbContext, a template service and a bus to construct.
/// </summary>
internal static class JobAlertSlots
{
    public const string Morning = "morning";
    public const string Noon = "noon";
    public const string Evening = "evening";

    /// <summary>
    /// The publisher's value for "every window is disabled, send whenever". Recognised so
    /// it resolves quietly rather than logging a warning on every message.
    /// </summary>
    public const string Always = "always";

    /// <summary>
    /// Normalise the slot named on the event.
    ///
    /// A FALLBACK RATHER THAN A THROW, deliberately. A slot this service has not been taught
    /// yet means the publisher was deployed first — an ordering problem, not a data problem
    /// — and losing somebody's digest to it is a worse outcome than sending them the morning
    /// wording. <paramref name="recognised"/> lets the caller log the difference without
    /// this method needing a logger.
    /// </summary>
    public static string Resolve(string? slot, out bool recognised)
    {
        string normalised = (slot ?? string.Empty).Trim().ToLowerInvariant();

        if (normalised is Morning or Noon or Evening)
        {
            recognised = true;
            return normalised;
        }

        // Absent and "always" are both expected, not anomalies worth warning about.
        recognised = normalised.Length == 0 || normalised == Always;
        return Morning;
    }
}
