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
    /// <para>
    /// Any existing file is deleted before the new one is created rather than truncated in place.
    /// <see cref="FileStreamOptions.UnixCreateMode"/> applies only when the file is actually
    /// created, so truncating a pre-existing world-readable file would quietly keep its permissions
    /// — the single most likely way for this to leak.
    /// </para>
    /// <para>
    /// A trailing newline is written so that <c>secret=$(cat "$file")</c> in bash yields exactly the
    /// secret: command substitution strips trailing newlines, and a file with no final newline is
    /// awkward for every other line-oriented tool.
    /// </para>
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
