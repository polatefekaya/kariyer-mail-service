using FluentValidation;

namespace Kariyer.Mail.Api.Features.Templates.BulkDeleteTemplates;

public sealed class BulkDeleteTemplatesValidator : AbstractValidator<BulkDeleteTemplatesRequest>
{
    public BulkDeleteTemplatesValidator()
    {
        RuleFor(x => x.TemplateIds)
            .NotEmpty().WithMessage("You must provide at least one template ID to delete.")
            .Must(ids => ids.Length <= 100).WithMessage("You can only bulk delete up to 100 templates at a time.");
    }
}