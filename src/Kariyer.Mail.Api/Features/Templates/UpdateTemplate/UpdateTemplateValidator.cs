using FluentValidation;

namespace Kariyer.Mail.Api.Features.Templates.UpdateTemplate;

public sealed class UpdateTemplateValidator : AbstractValidator<UpdateTemplateRequest>
{
    public UpdateTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SubjectTemplate).NotEmpty().MaximumLength(250);
        RuleFor(x => x.HtmlContent)
            .NotEmpty()
            .MaximumLength(500_000)
            .Must(html => !html.Contains("<script", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Script tags are prohibited.");
    }
}