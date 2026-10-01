using System.Globalization;

namespace Kariyer.Mail.Api.Features.Recruiting;

/// <summary>
/// Turns an interview event into the strings a template renders.
///
/// The wire carries UTC plus the IANA zone the interview was booked in, and the candidate has to
/// read the time in that zone — not in whatever zone this worker happens to run in. Everything
/// here is formatted once, so the three templates never have to agree on a date format.
/// </summary>
internal static class InterviewMailContent
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string DateTimeIn(DateTimeOffset instant, string timeZone) =>
        TimeZoneInfo.ConvertTime(instant, Resolve(timeZone)).ToString("d MMMM yyyy dddd, HH:mm", Turkish);

    public static string ZoneLabel(string timeZone) => Resolve(timeZone).Id;

    public static string Duration(int minutes) => minutes switch
    {
        60 => "1 saat",
        90 => "1,5 saat",
        120 => "2 saat",
        _ => $"{minutes.ToString(CultureInfo.InvariantCulture)} dakika",
    };

    public static string TypeLabel(string type) => type switch
    {
        "VIDEO" => "Video görüşme",
        "PHONE" => "Telefon görüşmesi",
        "IN_PERSON" => "Yüz yüze görüşme",
        _ => "Görüşme",
    };

    public static string LocationLabel(string type) => type switch
    {
        "VIDEO" => "Toplantı bağlantısı",
        "PHONE" => "Telefon",
        "IN_PERSON" => "Adres",
        _ => "Görüşme yeri",
    };

    /// <summary>Video interviews carry a link, the others an address or a number.</summary>
    public static string Location(string type, string videoUrl, string location) =>
        type == "VIDEO" ? videoUrl : location;

    private static TimeZoneInfo Resolve(string timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
