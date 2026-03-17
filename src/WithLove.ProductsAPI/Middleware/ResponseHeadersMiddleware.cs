namespace WithLove.ProductsAPI.Middleware;

/// <summary>
/// Middleware that adds standard security and caching headers to all responses.
///
/// Headers set:
/// - X-Content-Type-Options: nosniff (prevents MIME-sniffing attacks)
/// - Cache-Control: public max-age=300 for GET endpoints
/// - Cache-Control: private no-cache for POST/PUT/DELETE endpoints
/// </summary>
public class ResponseHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public ResponseHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add security header
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Add cache control based on HTTP method
        if (context.Request.Method == HttpMethods.Get)
        {
            context.Response.Headers["Cache-Control"] = "public, max-age=300";
        }
        else if (context.Request.Method == HttpMethods.Post ||
                 context.Request.Method == HttpMethods.Put ||
                 context.Request.Method == HttpMethods.Delete)
        {
            context.Response.Headers["Cache-Control"] = "private, no-cache";
        }

        await _next(context);
    }
}

/// <summary>
/// Extension method to register the response headers middleware in the pipeline.
/// </summary>
public static class ResponseHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseResponseHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ResponseHeadersMiddleware>();
    }
}
