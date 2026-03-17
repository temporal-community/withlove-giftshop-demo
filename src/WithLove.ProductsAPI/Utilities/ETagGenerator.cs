namespace WithLove.ProductsAPI.Utilities;

/// <summary>
/// Utility for generating and validating ETags from SQL Server rowVersion (byte[]).
/// ETags are used for optimistic concurrency control via If-Match/If-None-Match headers.
/// </summary>
public static class ETagGenerator
{
    /// <summary>
    /// Generates an ETag from SQL Server rowVersion timestamp.
    /// Format: W/"hash" (weak ETag) where hash is base64-encoded rowVersion.
    /// </summary>
    public static string GenerateETag(byte[] rowVersion)
    {
        if (rowVersion == null || rowVersion.Length == 0)
            throw new ArgumentException("RowVersion cannot be null or empty", nameof(rowVersion));

        // Convert rowVersion to base64 string for compact representation
        var base64 = Convert.ToBase64String(rowVersion);
        // Use weak ETag (W/) since we may have byte variations
        return $"W/\"{base64}\"";
    }

    /// <summary>
    /// Verifies if a client-provided ETag matches the current rowVersion.
    /// Strips W/ prefix if present and compares base64 representations.
    /// </summary>
    public static bool VerifyETag(string clientETag, byte[] currentRowVersion)
    {
        if (string.IsNullOrEmpty(clientETag))
            return false;

        if (currentRowVersion == null || currentRowVersion.Length == 0)
            return false;

        try
        {
            // Strip W/ prefix and quotes if present
            var etag = clientETag.StartsWith("W/") ? clientETag[2..] : clientETag;
            etag = etag.Trim('"');

            // Decode client ETag and compare with current rowVersion
            var clientVersion = Convert.FromBase64String(etag);
            return clientVersion.SequenceEqual(currentRowVersion);
        }
        catch
        {
            // If ETag decoding fails, consider it invalid
            return false;
        }
    }
}
