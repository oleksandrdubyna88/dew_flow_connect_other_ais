using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One place in <c>coai-mcp</c> builds and starts a child process: <c>ProcessLauncher</c>. The executable resolution
/// (npm shims), the UTF-8 streams without a byte-order mark and the environment rule (inherited, or the allowlist) are
/// written there once; a second launch path keeps a copy of some of them and drifts from the rest.
/// </summary>
/// <remarks>
/// The code gate of epic 4 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c> (2026-10-12, codex, Conventions and
/// Architecture): <c>AcpTransport.Start</c> built its own <c>ProcessStartInfo</c> — the encodings and the resolver copied,
/// the environment rule left out — because it needs a long-lived duplex pipe the one-shot <c>RunAsync</c> does not give.
/// </remarks>
public sealed partial class OnlyTheLauncherStartsAProcessTests
{
    [GeneratedRegex(@"new\s+ProcessStartInfo\s*\(|new\s+Process\s*\{|Process\.Start\s*\(")]
    private static partial Regex Starts();

    [Fact]
    public void NoProductionFile_ButTheLauncher_BuildsOrStartsAProcess()
    {
        var root = RepoPaths.Root();
        var offenders = new[] { Path.Combine(root, "src_mcp", "runners"), Path.Combine(root, "src_mcp", "src") }
            .SelectMany(static folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(static file => !IsBuildOutput(file) && Path.GetFileName(file) != "ProcessLauncher.cs")
            .SelectMany(static file => File.ReadLines(file)
                .Select((line, i) => (file, number: i + 1, line))
                .Where(static hit => !IsComment(hit.line) && Starts().IsMatch(hit.line)))
            .Select(hit => $"{Path.GetRelativePath(root, hit.file)}:{hit.number}: {hit.line.Trim()}")
            .ToList();

        offenders.Should().BeEmpty("every child process goes through ProcessLauncher, so its start rules are written once");
    }

    private static bool IsBuildOutput(string file) =>
        file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static part => part is "bin" or "obj");

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);
}
