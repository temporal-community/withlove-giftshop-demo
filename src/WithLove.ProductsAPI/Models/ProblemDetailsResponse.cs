namespace WithLove.ProductsAPI.Models;

/// <summary>
/// RFC 9457 Problem Details response model.
/// Standard format for error responses across all endpoints.
/// </summary>
public record ProblemDetailsResponse(
    /// <summary>
    /// URI identifying the problem type (e.g., "https://api.withlove.local/errors/validation-failed")
    /// </summary>
    string Type,

    /// <summary>
    /// Short human-readable summary of the problem type
    /// </summary>
    string Title,

    /// <summary>
    /// HTTP status code
    /// </summary>
    int Status,

    /// <summary>
    /// Human-readable explanation specific to this occurrence
    /// </summary>
    string Detail,

    /// <summary>
    /// Optional: URI reference to the specific occurrence (typically the request URI)
    /// </summary>
    string? Instance = null,

    /// <summary>
    /// Optional: Field-level validation errors (for 400 Bad Request)
    /// Key: field name, Value: array of error messages
    /// </summary>
    Dictionary<string, string[]>? Errors = null);
