using WithLove.ProductsAPI.Utilities;

namespace WithLove.ProductsAPI.Filters;

/// <summary>Validates the required X-WITHLOVE-API-VERSION header.</summary>
public class ApiVersionValidationFilter : IEndpointFilter
{
    private readonly ILogger<ApiVersionValidationFilter> _logger;
    private const string ApiVersionHeaderName = "X-WITHLOVE-API-VERSION";
    private const string DateFormatPattern = @"^\d{4}-\d{2}-\d{2}$";

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
            _logger.MissingApiVersionHeader(ApiVersionHeaderName);

            return ProblemDetailsResults.BadRequest(
                detail: $"The {ApiVersionHeaderName} header is required.",
                instance: request.Path.ToString());
        }

        var version = versionHeader.ToString();
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, DateFormatPattern))
        {
            _logger.InvalidApiVersionFormat(version);

            return ProblemDetailsResults.BadRequest(
                detail: $"The {ApiVersionHeaderName} header must be in YYYY-MM-DD format.",
                instance: request.Path.ToString());
        }

        return await next(context);
    }
}
