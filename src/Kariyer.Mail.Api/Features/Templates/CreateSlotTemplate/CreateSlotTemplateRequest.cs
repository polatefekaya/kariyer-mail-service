using FluentValidation;

namespace Kariyer.Mail.Api.Features.Templates.CreateSlotTemplate;

public sealed record CreateSlotTemplateRequest(string Name, string SubjectTemplate, string HtmlContent);

public sealed class CreateSlotTemplateValidator : AbstractValidator<CreateSlotTemplateRequest>
{
    public CreateSlotTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Template name is required.")
            .MaximumLength(100).WithMessage("Template name cannot exceed 100 characters.");

        RuleFor(x => x.SubjectTemplate)
            .NotEmpty().WithMessage("Subject template is required.")
            .MaximumLength(250).WithMessage("Subject cannot exceed 250 characters.");

        RuleFor(x => x.HtmlContent)
            .NotEmpty().WithMessage("HTML content cannot be empty.")
            .MaximumLength(500_000).WithMessage("HTML template is too large. Max 500KB allowed.")
            .Must(html => !html.Contains("<script", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Script tags are strictly prohibited in email templates.");
    }
}
