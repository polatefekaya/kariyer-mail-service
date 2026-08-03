using Kariyer.Mail.Api.Common.Configuration;

namespace Kariyer.Mail.Api.Features.Templates;

/// <summary>A single Scriban variable a template context guarantees to supply at render time.</summary>
public sealed record TemplatePlaceholder(string Name, string Example, string Description)
{
    public string ScribanSyntax => $"{{{{ {Name} }}}}";
}

/// <summary>
/// One authoring context. Either a system slot (an event-triggered email bound to a configured
/// slug) or the bulk/admin-sent context. <see cref="Placeholders"/> is the vocabulary the editor
/// offers and the preview seeds — it must mirror exactly what the corresponding consumer puts into
/// its <c>templateData</c> dictionary.
/// </summary>
public sealed record TemplateContextDefinition(
    string Context,
    string Description,
    string? SettingsKey,
    Func<EmailTemplateSettings, string>? SlugAccessor,
    IReadOnlyList<TemplatePlaceholder> Placeholders)
{
    public bool IsSystemSlot => SettingsKey is not null;
}

/// <summary>
/// The single source of truth for system slots and their placeholder vocabularies.
///
/// This used to live as four separate hardcoded lists (GetSystemTemplates, GetPlaceholderSets and
/// a ResolveSlug switch in each of AssignSystemSlot/UnassignSystemSlot). They drifted: the three
/// AccountDidNotCompleted step slots had no placeholder set at all, so the editor offered an empty
/// vocabulary and the preview silently fell back to the bulk-email variables — which is what made
/// every automated template look like it had a syntax error.
///
/// Keep this in sync with the consumers under Features/Account and with the legacy target resolver
/// (kariyer_zamani_backend/src/services/target/targetService.js) for the bulk context.
/// <see cref="TemplateContextResolver"/> asserts at startup that every SettingsKey here names a real
/// property on <see cref="EmailTemplateSettings"/> and that no property is left uncovered.
/// </summary>
internal static class TemplateContextRegistry
{
    public const string BulkEmailContext = "BulkEmail";

    // Shared by every consumer that only knows the recipient's display name.
    private static readonly TemplatePlaceholder[] FullNameOnly =
    [
        new("FullName", "Ahmet Yılmaz", "Alıcının tam adı"),
    ];

    // The three reminder steps are the same email at different intervals — same vocabulary.
    private static readonly TemplatePlaceholder[] DidNotCompletePlaceholders =
    [
        new("FullName",     "Ahmet Yılmaz", "Alıcının tam adı"),
        new("AccountType",  "company",      "Hesap tipi: company | employee"),
        new("ReminderStep", "2",            "Kaçıncı hatırlatma olduğu (1, 2, 3)"),
    ];

    public static readonly IReadOnlyList<TemplateContextDefinition> All =
    [
        new("AccountCreated",
            "Yeni bir hesap oluşturulduğunda gönderilir.",
            nameof(EmailTemplateSettings.AccountCreatedTemplateSlug),
            s => s.AccountCreatedTemplateSlug,
            [
                new("FullName",    "Ahmet Yılmaz", "Alıcının tam adı"),
                new("AccountType", "company",      "Hesap tipi: company | employee"),
            ]),

        new("AccountCompleted",
            "Kullanıcı profilini tamamladığında gönderilir.",
            nameof(EmailTemplateSettings.AccountCompletedTemplateSlug),
            s => s.AccountCompletedTemplateSlug,
            FullNameOnly),

        new("AccountApproved",
            "Hesap başvurusu onaylandığında gönderilir.",
            nameof(EmailTemplateSettings.AccountApprovedTemplateSlug),
            s => s.AccountApprovedTemplateSlug,
            [
                new("FullName",   "Ahmet Yılmaz",     "Alıcının tam adı"),
                new("ApprovedAt", "08.05.2026 12:00", "Onay zamanı"),
            ]),

        new("AccountRejected",
            "Hesap başvurusu reddedildiğinde gönderilir.",
            nameof(EmailTemplateSettings.AccountRejectedTemplateSlug),
            s => s.AccountRejectedTemplateSlug,
            [
                new("FullName",   "Ahmet Yılmaz",     "Alıcının tam adı"),
                new("Reason",     "Eksik evrak",      "Reddedilme gerekçesi"),
                new("RejectedAt", "08.05.2026 12:00", "Reddedilme zamanı"),
            ]),

        new("AccountFrozen",
            "Hesap dondurulduğunda gönderilir.",
            nameof(EmailTemplateSettings.AccountFrozenTemplateSlug),
            s => s.AccountFrozenTemplateSlug,
            [
                new("FullName", "Ahmet Yılmaz",     "Alıcının tam adı"),
                new("Reason",   "admin_initiated",  "Dondurma gerekçesi: admin_initiated | self_initiated"),
            ]),

        new("AccountDeleted",
            "Hesap silindiğinde gönderilir.",
            nameof(EmailTemplateSettings.AccountDeletedTemplateSlug),
            s => s.AccountDeletedTemplateSlug,
            FullNameOnly),

        new("AccountDidNotCompleted.Step1",
            "1. hatırlatma: Kullanıcı profili tamamlanmamış.",
            nameof(EmailTemplateSettings.AccountDidNotCompletedStep1TemplateSlug),
            s => s.AccountDidNotCompletedStep1TemplateSlug,
            DidNotCompletePlaceholders),

        new("AccountDidNotCompleted.Step2",
            "2. hatırlatma: Kullanıcı profili tamamlanmamış.",
            nameof(EmailTemplateSettings.AccountDidNotCompletedStep2TemplateSlug),
            s => s.AccountDidNotCompletedStep2TemplateSlug,
            DidNotCompletePlaceholders),

        new("AccountDidNotCompleted.Step3",
            "3. hatırlatma: Kullanıcı profili tamamlanmamış.",
            nameof(EmailTemplateSettings.AccountDidNotCompletedStep3TemplateSlug),
            s => s.AccountDidNotCompletedStep3TemplateSlug,
            DidNotCompletePlaceholders),

        new("AdminCompanyCompleted",
            "Bir şirket profilini tamamladığında yöneticiye bildirim gönderilir.",
            nameof(EmailTemplateSettings.AdminCompanyCompletedTemplateSlug),
            s => s.AdminCompanyCompletedTemplateSlug,
            [
                new("CompanyName",      "Kariyer Yazılım A.Ş.",        "Şirket adı"),
                new("Email",            "info@kariyer.net",            "Şirket e-posta adresi"),
                new("Phone",            "+90 212 000 0000",            "Şirket telefonu"),
                new("AuthorizedPerson", "Ahmet Yılmaz",                "Yetkili kişinin adı soyadı"),
                new("TaxIdNumber",      "1234567890",                  "Vergi kimlik numarası"),
                new("TaxOffice",        "Kadıköy",                     "Vergi dairesi"),
                new("Province",         "İstanbul",                    "İl"),
                new("Industry",         "Yazılım",                     "Sektör"),
                new("EmployeeCount",    "50-100",                      "Çalışan sayısı"),
                new("CompanyUid",       "01ARZ3NDEKTSV4RRFFQ69G5FAV",  "Şirket kimliği"),
                new("SubmittedAt",      "08.05.2026 12:00",            "Başvuru zamanı"),
            ]),

        new("AccountDeletionCancelled",
            "Hesap silme talebi iptal edildiğinde gönderilir.",
            nameof(EmailTemplateSettings.AccountDeletionCancelledTemplateSlug),
            s => s.AccountDeletionCancelledTemplateSlug,
            FullNameOnly),

        new("AccountEmailChanged",
            "Hesap e-posta adresi değiştirildiğinde gönderilir.",
            nameof(EmailTemplateSettings.AccountEmailChangedTemplateSlug),
            s => s.AccountEmailChangedTemplateSlug,
            [
                new("FullName", "Ahmet Yılmaz",       "Alıcının tam adı"),
                new("OldEmail", "eski@example.com",   "Önceki e-posta adresi"),
                new("NewEmail", "yeni@example.com",   "Yeni e-posta adresi"),
            ]),

        new("AccountPhoneChanged",
            "Hesap telefon numarası değiştirildiğinde gönderilir.",
            nameof(EmailTemplateSettings.AccountPhoneChangedTemplateSlug),
            s => s.AccountPhoneChangedTemplateSlug,
            [
                new("FullName", "Ahmet Yılmaz",     "Alıcının tam adı"),
                new("NewPhone", "+90 212 000 0000", "Yeni telefon numarası"),
            ]),

        new("AccountUsernameChanged",
            "Hesap kullanıcı adı değiştirildiğinde gönderilir.",
            nameof(EmailTemplateSettings.AccountUsernameChangedTemplateSlug),
            s => s.AccountUsernameChangedTemplateSlug,
            [
                new("FullName",    "Ahmet Yılmaz", "Alıcının tam adı"),
                new("NewUsername", "ahmetyilmaz",  "Yeni kullanıcı adı"),
            ]),

        // Admin-sent / bulk. Mirrors ResolvedTarget.Metadata as produced by the legacy backend
        // (targetService.js resolveTargets) plus the Email key the resolver injects. Every value
        // arrives as a string — booleans are "true"/"false", dates are "yyyy-MM-dd".
        new(BulkEmailContext,
            "Yönetici tarafından gönderilen toplu e-postalarda kullanılır.",
            SettingsKey: null,
            SlugAccessor: null,
            [
                new("Email",             "kullanici@example.com", "E-posta adresi"),
                new("FirstName",         "Ahmet",                 "Ad"),
                new("LastName",          "Yılmaz",                "Soyad"),
                new("Username",          "ahmetyilmaz",           "Kullanıcı adı"),
                new("BirthDate",         "1995-08-04",            "Doğum tarihi (yyyy-AA-gg)"),
                new("Gender",            "Erkek",                 "Cinsiyet"),
                new("Type",              "Employee",              "Kullanıcı tipi"),
                new("Title",             "Yazılım Geliştirici",   "Ünvan"),
                new("WorkingType",       "Tam Zamanlı",           "Çalışma şekli"),
                new("LookingJob",        "1",                     "İş arama durumu"),
                new("Phone",             "+90 555 123 4567",      "Telefon numarası"),
                new("CompanyEmail",      "ik@sirket.com",         "Şirket e-posta adresi"),
                new("Country",           "Türkiye",               "Ülke"),
                new("Province",          "İstanbul",              "İl"),
                new("Town",              "Beşiktaş",              "İlçe"),
                new("Neighbourhood",     "Levent",                "Mahalle"),
                new("Address",           "Levent Mah. No:1",      "Açık adres"),
                new("PhotoUrl",          "https://cdn.kariyerzamani.com/p/1.jpg", "Profil fotoğrafı (URL)"),
                new("BackgroundUrl",     "https://cdn.kariyerzamani.com/b/1.jpg", "Arkaplan fotoğrafı (URL)"),
                new("OneSignalPlayerId", "8f9c1e2a-0000-4a1b-9c3d-1f2e3d4c5b6a",  "OneSignal cihaz kimliği"),
                new("IsEmailVerified",   "true",                  "E-posta doğrulanmış mı (true/false)"),
                new("IsPhoneVerified",   "false",                 "Telefon doğrulanmış mı (true/false)"),
                new("AccountCreated",    "2026-01-15",            "Hesap oluşturma tarihi (yyyy-AA-gg)"),
            ]),
    ];

    /// <summary>The 14 event-triggered slots, in display order. Excludes the bulk context.</summary>
    public static readonly IReadOnlyList<TemplateContextDefinition> SystemSlots =
        All.Where(d => d.IsSystemSlot).ToArray();

    private static readonly Dictionary<string, TemplateContextDefinition> ByContext =
        All.ToDictionary(d => d.Context, StringComparer.Ordinal);

    private static readonly Dictionary<string, TemplateContextDefinition> BySettingsKey =
        SystemSlots.ToDictionary(d => d.SettingsKey!, StringComparer.Ordinal);

    public static TemplateContextDefinition BulkEmail => ByContext[BulkEmailContext];

    public static bool TryGetByContext(string? context, out TemplateContextDefinition definition)
    {
        if (context is not null) return ByContext.TryGetValue(context, out definition!);
        definition = null!;
        return false;
    }

    public static bool TryGetBySettingsKey(string? settingsKey, out TemplateContextDefinition definition)
    {
        if (settingsKey is not null) return BySettingsKey.TryGetValue(settingsKey, out definition!);
        definition = null!;
        return false;
    }
}
