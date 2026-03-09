using System.Collections.Generic;

namespace Kariyer.Mail.Api.Features.Templates.PreviewTemplate;

public sealed record PreviewTemplateRequest(
    Dictionary<string, object> DummyData 
);