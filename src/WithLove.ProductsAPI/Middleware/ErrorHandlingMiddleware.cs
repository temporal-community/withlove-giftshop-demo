using System.Text.Json;
using WithLove.ProductsAPI.Utilities;

namespace WithLove.ProductsAPI.Middleware;

/// <summary>
/// Global error handling middleware.
/// Catches unhandled exceptions and returns RFC 9457 Problem Details responses.
/// Logs exceptions for debugging and monitoring.
/// </summary>
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
        _logger.LogError(exception, "Unhandled exception occurred: {ExceptionType} - {Message}",
            exception.GetType().Name, exception.Message);

        var response = context.Response;
        response.ContentType = "application/problem+json";
        response.StatusCode = StatusCodes.Status500InternalServerError;

        // Determine if we should include exception details (development only)
        var includeDetails = _environment.IsDevelopment();

        // Create Problem Details response
        var problemDetails = ProblemDetailsResults.FromException(
            exception,
            instance: context.Request.Path,
            includeDetails: includeDetails);

        // Serialize and write response
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        var jsonResponse = JsonSerializer.Serialize(problemDetails, options);
        await response.WriteAsJsonAsync(problemDetails, options);
    }
}

/// <summary>
/// Extension method for registering error handling middleware.
/// </summary>
public static class ErrorHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseErrorHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ErrorHandlingMiddleware>();
    }
}
