using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GpStudio;

internal static class BundledFiles
{
    private static readonly Lazy<string> FormatterCache = new(PrepareFormatter);

    public static string FormatterDirectory => OperatingSystem.IsLinux()
        ? FormatterCache.Value : Path.Combine(AppContext.BaseDirectory, "formatter");

    public static byte[] Read(string name)
    {
        using Stream input = typeof(BundledFiles).Assembly.GetManifestResourceStream("GpStudio.Bundle." + name)
            ?? throw new IOException("Missing embedded file: " + name);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    public static string Text(string name) => Encoding.UTF8.GetString(Read(name));

    private static string PrepareFormatter()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException();
        byte[] binary = Read("formatter");
        byte[] style = Read("style.yaml");
        string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? "";
        if (!Path.IsPathFullyQualified(cache))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Path.IsPathFullyQualified(home))
                throw new IOException("Set XDG_CACHE_HOME to a writable absolute directory for the embedded formatter.");
            cache = Path.Combine(home, ".cache");
        }
        string version = Convert.ToHexString(SHA256.HashData(binary)) + "-" + Convert.ToHexString(SHA256.HashData(style));
        string directory = Path.Combine(cache, "gp-project-generator", version);
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Install(Path.Combine(directory, "clang-format"), binary, executable: true);
        Install(Path.Combine(directory, "style.yaml"), style, executable: false);
        return directory;
    }

    private static void Install(string path, byte[] content, bool executable)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException();
        UnixFileMode permissions = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (executable)
            permissions |= UnixFileMode.UserExecute;
        // Verify cached content before use; atomic replacement also permits concurrent launches.
        if (File.Exists(path) && new FileInfo(path).LinkTarget is null && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
        {
            File.SetUnixFileMode(path, permissions);
            return;
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            File.SetUnixFileMode(temporary, permissions);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
