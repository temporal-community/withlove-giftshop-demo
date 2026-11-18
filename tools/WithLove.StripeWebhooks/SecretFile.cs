namespace WithLove.StripeWebhooks;

/// <summary>
/// The only channel through which a signing secret leaves this process.
/// </summary>
/// <remarks>
/// stdout and stderr are not options: this tool runs inside <c>just deploy</c>, whose output lands
/// in terminal scrollback and CI logs. The file is the contract — one line, mode 0600.
/// </remarks>
internal static class SecretFile
{
    /// <summary>
    /// Writes <paramref name="secret"/> to <paramref name="path"/>, readable and writable by the
    /// owner only.
    /// </summary>
    /// <remarks>
    /// Replace the file so <see cref="FileStreamOptions.UnixCreateMode"/> sets its permissions.
    /// The trailing newline supports line-oriented tools and is stripped by shell substitution.
    /// </remarks>
    public static void Write(string path, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(path))
            File.Delete(path);

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        // UnixCreateMode throws PlatformNotSupportedException on Windows, where the inherited ACL
        // is the best available answer anyway.
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(secret);
        writer.Write('\n');
    }
}
