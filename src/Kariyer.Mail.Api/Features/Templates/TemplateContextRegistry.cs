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

    /// <summary>
    /// The three send windows carry identical data — only the wording differs, which is the
    /// entire reason they are separate slots. One list so they cannot drift from each other
    /// or from what JobAlertReadyConsumer actually supplies.
    ///
    /// UnsubscribeUrl is not optional in practice: these are the only standing-subscription
    /// emails the service sends, and a template authored without it would be a compliance
    /// problem rather than a cosmetic one.
    /// </summary>
    private static readonly TemplatePlaceholder[] JobAlertPlaceholders =
    [
        new("FullName",       "Ahmet Yılmaz", "Alıcının tam adı"),
        new("JobCount",       "7",            "Yeni eşleşen ilan sayısı"),
        new("AlertUrl",       "https://kariyerzamani.com/is-uyarilarim",
            "İş Uyarılarım sayfasının bağlantısı"),
        new("UnsubscribeUrl", "https://kariyerzamani.com/api/job_alerts/unsubscribe?token=…",
            "Tek tıkla abonelikten çıkma bağlantısı. ZORUNLU: şablonda mutlaka yer almalıdır."),
    ];

    // The three reminder steps are the same email at different intervals — same vocabulary.
    private static readonly TemplatePlaceholder[] DidNotCompletePlaceholders =
    [
        new("FullName",     "Ahmet Yılmaz", "Alıcının tam adı"),
        new("AccountType",  "company",      "Hesap tipi: company | employee"),
        new("ReminderStep", "2",            "Kaçıncı hatırlatma olduğu (1, 2, 3)"),
    ];

    /// <summary>
    /// What the three interview mails share. The recruiting service formats the instant in the
    /// zone the interview was booked in before it reaches a template, so a template never does
    /// date arithmetic — and never shows a candidate a time in the worker's zone.
    /// </summary>
    private static readonly TemplatePlaceholder[] InterviewPlaceholders =
    [
        new("CandidateName",     "Ahmet Yılmaz",             "Adayın adı soyadı"),
        new("CompanyName",       "Kariyer Yazılım A.Ş.",     "Görüşmeyi yapan şirket"),
        new("JobTitle",          "Frontend Developer",       "Başvurulan ilanın başlığı"),
        new("InterviewDateTime", "2 Ekim 2026 Cuma, 14:00",  "Görüşmenin tarihi ve saati"),
        new("TimeZone",          "Europe/Istanbul",          "Saatin ait olduğu zaman dilimi"),
        new("Duration",          "45 dakika",                "Görüşme süresi"),
        new("InterviewType",     "Video görüşme",            "Görüşme şekli"),
        new("LocationLabel",     "Toplantı bağlantısı",      "Görüşme yeri alanının başlığı"),
        new("Location",          "https://meet.google.com/…", "Toplantı bağlantısı, adres ya da telefon"),
        new("Message",           "Görüşmede portföyünüzü konuşacağız.", "Yetkilinin adaya notu; boş olabilir"),
    ];

    /// <summary>
    /// What a company-side interview answer mail carries. Accepted and declined share it — the
    /// wording is the only difference, which is the entire reason they are separate slots.
    /// </summary>
    private static readonly TemplatePlaceholder[] InterviewAnswerPlaceholders =
    [
        new("RecipientName",     "Polat Kaya",               "E-postayı alan yetkilinin adı"),
        new("CandidateName",     "Ahmet Yılmaz",             "Yanıt veren adayın adı soyadı"),
        new("CompanyName",       "Kariyer Yazılım A.Ş.",     "Görüşmeyi yapan şirket"),
        new("JobTitle",          "Frontend Developer",       "Başvurulan ilanın başlığı"),
        new("InterviewDateTime", "2 Ekim 2026 Cuma, 14:00",  "Görüşmenin tarihi ve saati"),
        new("TimeZone",          "Europe/Istanbul",          "Saatin ait olduğu zaman dilimi"),
        new("InterviewType",     "Video görüşme",            "Görüşme şekli"),
        new("ReviewUrl",         "https://basvuru.kariyerzamani.com/ilanlar/…",
            "Şirket panelinde ilanın mülakatlarına giden bağlantı"),
    ];

    /// <summary>
    /// What the three candidate-facing decision mails share. Offer and hire need nothing else;
    /// a rejection adds whether it came after an interview, which is a different letter.
    /// </summary>
    private static readonly TemplatePlaceholder[] StageDecisionPlaceholders =
    [
        new("CandidateName", "Ahmet Yılmaz",         "Adayın adı soyadı"),
        new("CompanyName",   "Kariyer Yazılım A.Ş.", "Başvurulan şirket"),
        new("JobTitle",      "Frontend Developer",   "Başvurulan ilanın başlığı"),
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

        new("JobAlertMorning",
            "Sabah gönderim aralığında, adayın çalışma tercihlerine uyan yeni ilanlar için gönderilir.",
            nameof(EmailTemplateSettings.JobAlertMorningTemplateSlug),
            s => s.JobAlertMorningTemplateSlug,
            JobAlertPlaceholders),

        new("JobAlertNoon",
            "Öğle gönderim aralığında, adayın çalışma tercihlerine uyan yeni ilanlar için gönderilir.",
            nameof(EmailTemplateSettings.JobAlertNoonTemplateSlug),
            s => s.JobAlertNoonTemplateSlug,
            JobAlertPlaceholders),

        new("JobAlertEvening",
            "Akşam gönderim aralığında, adayın çalışma tercihlerine uyan yeni ilanlar için gönderilir.",
            nameof(EmailTemplateSettings.JobAlertEveningTemplateSlug),
            s => s.JobAlertEveningTemplateSlug,
            JobAlertPlaceholders),

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

        new("InterviewInvited",
            "Aday bir mülakata davet edildiğinde gönderilir.",
            nameof(EmailTemplateSettings.InterviewInvitedTemplateSlug),
            s => s.InterviewInvitedTemplateSlug,
            [
                .. InterviewPlaceholders,
                new("InvitedByName", "Polat Kaya", "Daveti gönderen yetkilinin adı"),
                new("AcceptUrl",  "https://api.kariyerzamani.com/api/recruiting/interviews/…/confirmation/accept?token=…",
                    "Adayın katılacağını bildirdiği tek tıklık bağlantı. Yalnızca adaya gönderilir."),
                new("DeclineUrl", "https://api.kariyerzamani.com/api/recruiting/interviews/…/confirmation/decline?token=…",
                    "Adayın katılamayacağını bildirdiği tek tıklık bağlantı. Yalnızca adaya gönderilir."),
            ]),

        new("InterviewRescheduled",
            "Adaya iletilmiş bir mülakatın saati, süresi ya da şekli değiştiğinde gönderilir.",
            nameof(EmailTemplateSettings.InterviewRescheduledTemplateSlug),
            s => s.InterviewRescheduledTemplateSlug,
            [
                .. InterviewPlaceholders,
                new("PreviousDateTime", "2 Ekim 2026 Cuma, 11:00",
                    "Değişiklikten önceki tarih ve saat. Adayın takvimindeki kaydı budur."),
                new("ChangedByName", "Polat Kaya", "Değişikliği yapan yetkilinin adı"),
                new("AcceptUrl",  "https://api.kariyerzamani.com/api/recruiting/interviews/…/confirmation/accept?token=…",
                    "Yeni saat için adayın onay bağlantısı. Yalnızca adaya gönderilir."),
                new("DeclineUrl", "https://api.kariyerzamani.com/api/recruiting/interviews/…/confirmation/decline?token=…",
                    "Yeni saat için adayın ret bağlantısı. Yalnızca adaya gönderilir."),
            ]),

        new("InterviewCancelled",
            "Planlanmış bir mülakat şirket tarafından iptal edildiğinde gönderilir.",
            nameof(EmailTemplateSettings.InterviewCancelledTemplateSlug),
            s => s.InterviewCancelledTemplateSlug,
            [
                new("CandidateName",     "Ahmet Yılmaz",            "Adayın adı soyadı"),
                new("CompanyName",       "Kariyer Yazılım A.Ş.",    "Görüşmeyi iptal eden şirket"),
                new("JobTitle",          "Frontend Developer",      "Başvurulan ilanın başlığı"),
                new("InterviewDateTime", "2 Ekim 2026 Cuma, 14:00", "İptal edilen görüşmenin tarihi ve saati"),
                new("TimeZone",          "Europe/Istanbul",         "Saatin ait olduğu zaman dilimi"),
                new("Message",           "Yeni bir tarihle tekrar ulaşacağız.", "Yetkilinin adaya notu; boş olabilir"),
                new("CancelledByName",   "Polat Kaya",              "İptali yapan yetkilinin adı"),
            ]),

        new("InterviewAnswered.Accepted",
            "Aday mülakat davetini kabul ettiğinde şirket tarafına gönderilir.",
            nameof(EmailTemplateSettings.InterviewAcceptedTemplateSlug),
            s => s.InterviewAcceptedTemplateSlug,
            InterviewAnswerPlaceholders),

        new("InterviewAnswered.Declined",
            "Aday mülakat davetini reddettiğinde şirket tarafına gönderilir.",
            nameof(EmailTemplateSettings.InterviewDeclinedTemplateSlug),
            s => s.InterviewDeclinedTemplateSlug,
            InterviewAnswerPlaceholders),

        new("ApplicationSubmitted",
            "Aday bir ilana başvurduğunda adaya gönderilir.",
            nameof(EmailTemplateSettings.ApplicationSubmittedTemplateSlug),
            s => s.ApplicationSubmittedTemplateSlug,
            [
                new("CandidateName",   "Ahmet Yılmaz",         "Adayın adı soyadı"),
                new("CompanyName",     "Kariyer Yazılım A.Ş.", "Başvurulan şirket"),
                new("JobTitle",        "Frontend Developer",   "Başvurulan ilanın başlığı"),
                new("SubmittedAt",     "2 Ekim 2026 Cuma, 14:00", "Başvuru zamanı"),
                new("ApplicationsUrl", "https://kariyerzamani.com/basvurularim", "Adayın Başvurularım sayfası"),
            ]),

        new("ApplicationSubmitted.Company",
            "Bir ilana yeni başvuru geldiğinde şirkete gönderilir.",
            nameof(EmailTemplateSettings.ApplicationSubmittedCompanyTemplateSlug),
            s => s.ApplicationSubmittedCompanyTemplateSlug,
            [
                new("CompanyName",  "Kariyer Yazılım A.Ş.", "İlanı veren şirket"),
                new("CandidateName", "Ahmet Yılmaz",        "Başvuran adayın adı soyadı"),
                new("JobTitle",     "Frontend Developer",   "Başvurulan ilanın başlığı"),
                new("SubmittedAt",  "2 Ekim 2026 Cuma, 14:00", "Başvuru zamanı"),
                new("IsQuickApply", "false",                "Hızlı başvuru mu (true/false); hızlı başvuruda ön yazı yoktur"),
                new("ReviewUrl",    "https://basvuru.kariyerzamani.com/ilanlar/…",
                    "Şirket panelinde ilanın başvurularına giden bağlantı"),
            ]),

        new("ApplicationWithdrawn",
            "Aday başvurusunu geri çektiğinde şirkete gönderilir.",
            nameof(EmailTemplateSettings.ApplicationWithdrawnTemplateSlug),
            s => s.ApplicationWithdrawnTemplateSlug,
            [
                new("CompanyName",   "Kariyer Yazılım A.Ş.", "İlanı veren şirket"),
                new("CandidateName", "Ahmet Yılmaz",         "Başvurusunu geri çeken adayın adı soyadı"),
                new("JobTitle",      "Frontend Developer",   "Başvurulan ilanın başlığı"),
                new("WithdrawnAt",   "2 Ekim 2026 Cuma, 14:00", "Geri çekme zamanı"),
                new("ReviewUrl",     "https://basvuru.kariyerzamani.com/ilanlar/…",
                    "Şirket panelinde ilanın başvurularına giden bağlantı"),
            ]),

        // The three decisions below are held before sending (PendingStageMail), so a move the
        // recruiter undoes within the hold never reaches the candidate.
        new("ApplicationStage.Offer",
            "Adaya teklif verildiğinde, kısa bir bekleme süresinden sonra adaya gönderilir.",
            nameof(EmailTemplateSettings.ApplicationOfferTemplateSlug),
            s => s.ApplicationOfferTemplateSlug,
            StageDecisionPlaceholders),

        new("ApplicationStage.Hired",
            "Aday işe alındı olarak işaretlendiğinde, kısa bir bekleme süresinden sonra adaya gönderilir.",
            nameof(EmailTemplateSettings.ApplicationHiredTemplateSlug),
            s => s.ApplicationHiredTemplateSlug,
            StageDecisionPlaceholders),

        new("ApplicationStage.Rejected",
            "Aday reddedildiğinde, kısa bir bekleme süresinden sonra adaya gönderilir.",
            nameof(EmailTemplateSettings.ApplicationRejectedTemplateSlug),
            s => s.ApplicationRejectedTemplateSlug,
            [
                .. StageDecisionPlaceholders,
                new("AfterInterview", "true",
                    "Ret mülakattan sonra mı geldi (true/false); erken elemeyle mülakat sonrası ret farklı yazılır"),
            ]),

        // The only slot fed by a PUBLIC endpoint rather than by a bus event. Its vocabulary
        // must mirror the templateData dictionary in SubmitLeadEndpoint exactly — the point of
        // this registry is that the editor offers what the sender actually supplies.
        new("ServiceLead",
            "Hizmet sayfalarındaki formdan talep geldiğinde ekibe gönderilir.",
            nameof(EmailTemplateSettings.ServiceLeadTemplateSlug),
            s => s.ServiceLeadTemplateSlug,
            [
                new("FullName",    "Ahmet Yılmaz",                     "Talebi gönderen kişinin adı"),
                new("CompanyName", "Örnek Lojistik A.Ş.",              "Şirket adı"),
                new("Email",       "ahmet@ornek.com",                  "İletişim e-postası"),
                new("Phone",       "+90 555 123 4567",                 "İletişim telefonu"),
                new("Message",     "200 kişilik depo ekibi arıyoruz.", "Serbest metin mesaj (boşsa 'Belirtilmedi')"),
                new("PageLabel",   "Fuar ve Etkinlik Personeli Temini","Talebin geldiği sayfanın adı"),
                new("PagePath",    "/fuar-etkinlik-personeli-temini",  "Talebin geldiği sayfanın yolu"),
                new("Locale",      "tr",                               "Formun doldurulduğu dil"),
                new("SubmittedAt", "08.05.2026 12:00",                 "Gönderim zamanı (UTC)"),
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

    /// <summary>The event- or form-triggered slots, in display order. Excludes the bulk context.</summary>
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
