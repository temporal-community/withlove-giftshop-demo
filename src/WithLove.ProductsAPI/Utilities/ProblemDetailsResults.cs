using Microsoft.AspNetCore.Http.HttpResults;
using WithLove.ProductsAPI.Models;

namespace WithLove.ProductsAPI.Utilities;

/// <summary>
/// Helper utility for creating RFC 9457 Problem Details response results.
/// Provides factory methods for common HTTP error scenarios.
/// </summary>
public static class ProblemDetailsResults
{
    private const string BaseErrorUri = "https://api.withlove.local/errors";

    /// <summary>
    /// Creates a 400 Bad Request response with validation or business logic errors.
    /// </summary>
    public static BadRequest<ProblemDetailsResponse> BadRequest(
        string detail,
        string? instance = null,
        Dictionary<string, string[]>? errors = null)
    {
        var response = new ProblemDetailsResponse(
            Type: $"{BaseErrorUri}/bad-request",
            Title: "Bad Request",
            Status: 400,
            Detail: detail,
            Instance: instance,
            Errors: errors);

        return TypedResults.BadRequest(response);
    }

    /// <summary>
    /// Creates a 400 Bad Request response for validation failures.
    /// </summary>
    public static BadRequest<ProblemDetailsResponse> ValidationFailed(
        Dictionary<string, string[]> errors,
        string? instance = null)
    {
        var response = new ProblemDetailsResponse(
            Type: $"{BaseErrorUri}/validation-failed",
            Title: "Validation Failed",
            Status: 400,
            Detail: "One or more validation errors occurred.",
            Instance: instance,
            Errors: errors);

        return TypedResults.BadRequest(response);
    }

    /// <summary>
    /// Creates a 404 Not Found response.
    /// </summary>
    public static NotFound<ProblemDetailsResponse> NotFound(
        string detail,
        string? instance = null)
    {
        var response = new ProblemDetailsResponse(
            Type: $"{BaseErrorUri}/not-found",
            Title: "Not Found",
            Status: 404,
            Detail: detail,
            Instance: instance);

        return TypedResults.NotFound(response);
    }

    /// <summary>
    /// Creates a 409 Conflict response (e.g., SKU uniqueness violation).
    /// </summary>
    public static Conflict<ProblemDetailsResponse> Conflict(
        string detail,
        string? instance = null)
    {
        var response = new ProblemDetailsResponse(
            Type: $"{BaseErrorUri}/conflict",
            Title: "Conflict",
            Status: 409,
            Detail: detail,
            Instance: instance);

        return TypedResults.Conflict(response);
    }

    /// <summary>
    /// Creates a 412 Precondition Failed response (ETag mismatch, If-Match failure).
    /// </summary>
    public static StatusCodeHttpResult PreconditionFailed(
        string detail,
        string? instance = null)
    {
        var response = new ProblemDetailsResponse(
            Type: $"{BaseErrorUri}/precondition-failed",
            Title: "Precondition Failed",
            Status: 412,
            Detail: detail,
            Instance: instance);

        return TypedResults.StatusCode(412);
    }

    /// <summary>
    /// Creates a 500 Internal Server Error response.
    /// </summary>
    public static StatusCodeHttpResult InternalServerError(
        string detail = "An unexpected error occurred.",
        string? instance = null)
    {
        var response = new ProblemDetailsResponse(
            Type: $"{BaseErrorUri}/internal-server-error",
            Title: "Internal Server Error",
            Status: 500,
            Detail: detail,
            Instance: instance);

        return TypedResults.StatusCode(500);
    }

    /// <summary>
    /// Converts an exception to a Problem Details response (for middleware).
    /// </summary>
    public static ProblemDetailsResponse FromException(
        Exception ex,
        string? instance = null,
        bool includeDetails = false)
    {
        return new ProblemDetailsResponse(
            Type: $"{BaseErrorUri}/internal-server-error",
            Title: "Internal Server Error",
            Status: 500,
            Detail: includeDetails ? ex.Message : "An unexpected error occurred.",
            Instance: instance);
    }
}
