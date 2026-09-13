using System.IO;

namespace WithLove.StripeWebhooks.Tests.Unit;

/// <summary>
/// The secret file is the only channel through which a signing secret leaves the tool, so its
/// permissions and its exact contents are part of the contract with the justfile.
/// </summary>
public class SecretFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"withlove-stripe-webhooks-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private string TempPath(string name) => Path.Combine(_directory, name);

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Write_ProducesExactlyTheSecretAndOneTrailingNewline()
    {
        // `secret=$(cat "$file")` in bash strips the trailing newline, so this is exactly the secret
        // from the caller's point of view -- while still being a well-formed text file.
        var path = TempPath("whsec");

        SecretFile.Write(path, "whsec_abc123");

        File.ReadAllText(path).Should().Be("whsec_abc123\n");
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Write_CreatesMissingDirectories()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "whsec");

        SecretFile.Write(path, "whsec_abc123");

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Write_IsOwnerReadWriteOnly()
    {
        if (OperatingSystem.IsWindows())
            return;

        var path = TempPath("whsec");

        SecretFile.Write(path, "whsec_abc123");

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    [Trait(TestTraits.Category, TestTraits.Unit)]
    [Trait(TestTraits.Feature, TestTraits.StripeWebhooks)]
    public void Write_TightensThePermissionsOfAPreExistingWorldReadableFile()
    {
        if (OperatingSystem.IsWindows())
            return;

        // This is the single most likely way for the secret to leak. UnixCreateMode applies only
        // when the file is actually created, so truncating in place would silently keep 0644 --
        // which is exactly what a re-run over a file left by an earlier, sloppier write would do.
        var path = TempPath("whsec");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "whsec_stale");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        SecretFile.Write(path, "whsec_fresh");

        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.ReadAllText(path).Should().Be("whsec_fresh\n");
    }
}
