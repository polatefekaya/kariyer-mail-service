using Kariyer.Mail.Api.Common.Web;

namespace Kariyer.Mail.Api.Features.Templates.GetPlaceholderSets;

/// <summary>
/// Exposes the authoring vocabulary per context so the admin editor can offer the right variables
/// and the preview can seed the right example data. Backed by <see cref="TemplateContextRegistry"/>
/// — do not reintroduce a local list here.
/// </summary>
internal sealed class GetPlaceholderSetsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("templates/placeholder-sets", (string? context) =>
        {
            IEnumerable<TemplateContextDefinition> definitions = TemplateContextRegistry.All;

            if (!string.IsNullOrWhiteSpace(context))
            {
                if (!TemplateContextRegistry.TryGetByContext(context, out TemplateContextDefinition match))
                    return Results.NotFound(new { Message = $"Unknown template context '{context}'." });

                definitions = [match];
            }

            TemplatePlaceholderSetDto[] sets = definitions
                .Select(d => new TemplatePlaceholderSetDto(
                    d.Context,
                    d.Description,
                    d.IsSystemSlot,
                    d.Placeholders
                        .Select(p => new PlaceholderDto(p.Name, p.ScribanSyntax, p.Example, p.Description))
                        .ToArray()))
                .ToArray();

            return Results.Ok(sets);
        })
        .WithTags("Templates");
    }
}
