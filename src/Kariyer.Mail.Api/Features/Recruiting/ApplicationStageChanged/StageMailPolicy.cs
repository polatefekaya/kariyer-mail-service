using Kariyer.Mail.Api.Common.Configuration;

namespace Kariyer.Mail.Api.Features.Recruiting.ApplicationStageChanged;

/// <summary>
/// Which pipeline moves the candidate hears about, and with which slot.
///
/// Mirrors the recruiting service's stage machine without depending on it: the stage names are
/// the shared wire vocabulary, and the publisher deliberately does not decide what is worth an
/// e-mail (see ApplicationStageChangedEvent).
/// </summary>
internal static class StageMailPolicy
{
    private const string Interview = "INTERVIEW";
    private const string Offer = "OFFER";
    private const string Hired = "HIRED";
    private const string Rejected = "REJECTED";

    /// <summary>
    /// The settings key of the slot to send, or null when the move is silent:
    /// <list type="bullet">
    ///   <item>housekeeping moves (NEW → REVIEWING, anything → HOLD);</item>
    ///   <item>INTERVIEW, which the invitation announces;</item>
    ///   <item>a correction out of HIRED or REJECTED — undoing a decision the candidate may
    ///         already have been told about must not send a second, contradicting letter.
    ///         HIRED → REJECTED is the exception: that is a new decision, not an undo.</item>
    /// </list>
    /// </summary>
    public static string? SettingsKeyFor(string fromStage, string toStage)
    {
        if (fromStage == toStage)
        {
            return null;
        }

        bool correction = fromStage is Hired or Rejected && toStage != Rejected;

        if (correction)
        {
            return null;
        }

        return toStage switch
        {
            Offer => nameof(EmailTemplateSettings.ApplicationOfferTemplateSlug),
            Hired => nameof(EmailTemplateSettings.ApplicationHiredTemplateSlug),
            Rejected => nameof(EmailTemplateSettings.ApplicationRejectedTemplateSlug),
            _ => null,
        };
    }

    /// <summary>A rejection after the candidate met the company reads differently from an early one.</summary>
    public static bool IsAfterInterview(string fromStage) => fromStage is Interview or Offer or Hired;
}
