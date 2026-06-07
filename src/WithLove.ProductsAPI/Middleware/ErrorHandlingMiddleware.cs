using System.Text.Json;
using WithLove.ProductsAPI.Utilities;

namespace WithLove.ProductsAPI.Middleware;

/// <summary>Converts unhandled exceptions to RFC 9457 Problem Details responses.</summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.UnhandledException(exception, exception.GetType().Name, exception.Message);

        var response = context.Response;
        response.ContentType = "application/problem+json";
        response.StatusCode = StatusCodes.Status500InternalServerError;

        var includeDetails = _environment.IsDevelopment();

        var problemDetails = ProblemDetailsResults.FromException(
            exception,
            instance: context.Request.Path,
            includeDetails: includeDetails);

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        await response.WriteAsJsonAsync(problemDetails, options);
    }
}

/// <summary>Registers global Problem Details error handling.</summary>
public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ErrorHandlingMiddleware>();
    }
}
