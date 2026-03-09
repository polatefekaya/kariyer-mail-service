namespace Kariyer.Mail.Api.Common.Web;

public sealed class UlidRouteConstraint : IRouteConstraint
{
    public bool Match(
        HttpContext? httpContext, 
        IRouter? route, 
        string routeKey, 
        RouteValueDictionary values, 
        RouteDirection routeDirection)
    {
        if (values.TryGetValue(routeKey, out object? value) && value != null)
        {
            string stringValue = value.ToString() ?? string.Empty;
            return Ulid.TryParse(stringValue, out _);
        }

        return false;
    }
}