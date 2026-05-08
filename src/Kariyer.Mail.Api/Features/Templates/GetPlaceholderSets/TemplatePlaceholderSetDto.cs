namespace Kariyer.Mail.Api.Features.Templates.GetPlaceholderSets;

public sealed record TemplatePlaceholderSetDto(
    string Context,
    string Description,
    IReadOnlyList<PlaceholderDto> Placeholders);

public sealed record PlaceholderDto(
    string Name,
    string ScribanSyntax,
    string Example);
