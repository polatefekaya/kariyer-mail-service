namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

/// <summary>
/// Shared HTTP shaping for both preview endpoints.
///
/// Unresolved variables come back as a <c>200</c> carrying the rendered output and the list of
/// names, not as an error: the author still needs to see their template, and the send path would
/// have rendered those variables as empty anyway. Only content Scriban cannot execute is a 400.
/// </summary>
internal static class PreviewResults
{
    public static IResult ToHttpResult(TemplatePreviewResult result) => result.ErrorKind switch
    {
        TemplatePreviewErrorKind.None => Results.Ok(new
        {
            result.RenderedSubject,
            result.RenderedHtml,
            result.Context,
            result.UnknownVariables,
            result.UsedVariables
        }),

        TemplatePreviewErrorKind.UnknownContext => Results.BadRequest(new
        {
            result.Message,
            ErrorKind = nameof(TemplatePreviewErrorKind.UnknownContext),
            result.Context
        }),

        _ => Results.BadRequest(new
        {
            result.Message,
            ErrorKind = result.ErrorKind.ToString(),
            result.Context,
            result.Issues
        })
    };
}
