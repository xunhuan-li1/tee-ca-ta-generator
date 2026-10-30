// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GpStudio;

internal static class CSourceFormat
{
    private static readonly ConcurrentDictionary<string, string> Results = new();

    public static string Format(string source) => Results.GetOrAdd(source, Run);

    private static string Run(string source)
    {
        string directory = BundledFiles.FormatterDirectory;
        var settings = new ProcessStartInfo(Path.Combine(directory, OperatingSystem.IsWindows() ? "clang-format.exe" : "clang-format"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        settings.ArgumentList.Add("--style=file:" + Path.Combine(directory, "style.yaml"));
        settings.ArgumentList.Add("--assume-filename=generated.c");
        using var child = Process.Start(settings) ?? throw new IOException("Unable to start C formatter.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        child.StandardInput.Write(source);
        child.StandardInput.Close();
        if (!child.WaitForExit(15000))
        {
            child.Kill(entireProcessTree: true);
            throw new IOException("C formatter exceeded its time limit.");
        }
        string formatted = output.GetAwaiter().GetResult();
        string diagnostic = error.GetAwaiter().GetResult();
        if (child.ExitCode != 0 || string.IsNullOrWhiteSpace(formatted))
            throw new IOException("C formatter failed: " + diagnostic);
        return CParagraphs.Apply(formatted);
    }
}
