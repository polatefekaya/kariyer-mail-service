using FluentValidation;
using Kariyer.Mail.Api.Features.Leads.Contracts;

namespace Kariyer.Mail.Api.Features.Leads;

// PUBLIC, like every other validator here, and not by style preference:
// `AddValidatorsFromAssembly` in Program.cs defaults to `includeInternalTypes: false`, so an
// internal validator is silently skipped at registration. Nothing fails at build or at boot —
// the endpoint just 400s on its first real request with "Unable to resolve service for type
// IValidator<SubmitLeadRequest>", because ValidationFilter<T> cannot be constructed.
public sealed class SubmitLeadValidator : AbstractValidator<SubmitLeadRequest>
{
    public SubmitLeadValidator()
    {
        // Upper bounds everywhere. This endpoint is open to the internet, and the values land
        // in a rendered email — an unbounded "message" is both a storage problem and a way to
        // make a notification unreadable.
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Ad soyad zorunludur.")
            .MaximumLength(120);

        RuleFor(x => x.CompanyName)
            .NotEmpty().WithMessage("Şirket adı zorunludur.")
            .MaximumLength(160);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta zorunludur.")
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi girin.")
            .MaximumLength(254); // RFC 5321 maximum for a forward path.

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Telefon zorunludur.")
            .MaximumLength(32);

        RuleFor(x => x.Message)
            .MaximumLength(4000);

        // Not free text: it identifies which of the seven service pages produced the lead, so
        // sales knows what the enquiry is about. Constrained so it cannot smuggle a URL to
        // another host into the notification body.
        RuleFor(x => x.PagePath)
            .NotEmpty()
            .MaximumLength(256)
            .Must(p => p.StartsWith('/') && !p.StartsWith("//"))
            .WithMessage("PagePath site-relative olmalıdır.");

        RuleFor(x => x.PageLabel)
            .NotEmpty()
            .MaximumLength(160);

        RuleFor(x => x.Locale)
            .MaximumLength(8);

        // The honeypot is checked in the endpoint, not here: a validation failure would tell a
        // bot exactly which field gave it away. See the endpoint for why it returns 202.
    }
}
