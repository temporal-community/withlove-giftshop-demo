using WithLove.ProductsAPI.Utilities;

namespace WithLove.ProductsAPI.Filters;

/// <summary>
/// Endpoint filter that validates the X-WITHLOVE-API-VERSION header.
/// Ensures header is present and follows YYYY-MM-DD format.
/// Returns 400 Bad Request with RFC 9457 Problem Details if invalid.
/// </summary>
public class ApiVersionValidationFilter : IEndpointFilter
{
    private readonly ILogger<ApiVersionValidationFilter> _logger;
    private const string ApiVersionHeaderName = "X-WITHLOVE-API-VERSION";
    private const string DateFormatPattern = @"^\d{4}-\d{2}-\d{2}$"; // YYYY-MM-DD

    public ApiVersionValidationFilter(ILogger<ApiVersionValidationFilter> logger)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var request = httpContext.Request;

        if (!request.Headers.TryGetValue(ApiVersionHeaderName, out var versionHeader))
        {
            _logger.LogWarning("Missing {HeaderName} header in request", ApiVersionHeaderName);

            return ProblemDetailsResults.BadRequest(
                detail: $"The {ApiVersionHeaderName} header is required.",
                instance: request.Path.ToString());
        }

        var version = versionHeader.ToString();
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, DateFormatPattern))
        {
            _logger.LogWarning("Invalid API version format: {Version}", version);

            return ProblemDetailsResults.BadRequest(
                detail: $"The {ApiVersionHeaderName} header must be in YYYY-MM-DD format.",
                instance: request.Path.ToString());
        }

        // Version is valid, proceed with handler
        return await next(context);
    }
}
