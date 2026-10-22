using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every pid file the fake CLI writes is written WHOLE OR NOT AT ALL, and every wait for one waits for its CONTENT (C24 of
/// <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E5.md</c>, §3.4). <c>File.WriteAllText</c> creates the file and only then
/// writes it, so a poller that waits for <c>File.Exists</c> can read it while it is EMPTY — the CI run 39760284371 failure
/// (<c>FormatException: The input string '' was not in a correct format.</c>, exit 97). The one sanctioned writer is
/// <c>Serving.WritePidFile</c> (sibling temp name, then a move); the one sanctioned wait is <c>PidWritten</c> (exists AND parses).
/// </summary>
/// <remarks>
/// A source scan in the <c>NoTestHoldsAnInheritableHandleTests</c> shape over <c>src_mcp/tests_fakecli/*.cs</c>. Comments are not code. A line
/// naming an <c>.eof</c> marker is not a pid file. <c>Program.cs</c>'s <c>FAKECLI_VERSION_PID</c> write is exempt on purpose: a <c>--version</c>
/// writes its own pid once and the test reads it AFTER the launcher killed that process — nobody polls it.
/// </remarks>
public sealed class EveryPidFileTheFakeWritesIsWrittenWholeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(1);

    /// <summary>A direct write: the call whose FIRST argument is a pid-named identifier or a <c>.pid</c> path, or any write inside a <c>WritePid…(</c> one-liner.</summary>
    private static readonly Regex DirectWrite = new(@"\bFile\.WriteAll(?:Text|Lines)\(\s*(?:\w*[Pp]id\w*\s*,|[^;]*?\.pid"")|\b[Ww]rite[Pp]id\w*\(.*\bFile\.WriteAll(?:Text|Lines)\(", RegexOptions.CultureInvariant, Bound);

    /// <summary>A wait for a pid file's existence alone: <c>File.Exists(</c> over a <c>.pid</c> path or a pid-named identifier.</summary>
    private static readonly Regex ExistenceWait = new(@"\bFile\.Exists\(.*(?:\.pid""|[Pp]id[A-Z]?\w*\))", RegexOptions.CultureInvariant, Bound);

    private static IEnumerable<string> FakeSources() =>
        Directory.EnumerateFiles(Path.Combine(RepoPaths.Root(), "src_mcp", "tests_fakecli"), "*.cs", SearchOption.TopDirectoryOnly);

    private static bool IsCode(string line) => !line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static bool IsExempt(string file, string line) =>
        line.Contains("\".eof\"", StringComparison.Ordinal)
        || (Path.GetFileName(file) == "Serving.cs" && line.Contains("File.WriteAllLines(writing, lines)", StringComparison.Ordinal))
        || (Path.GetFileName(file) == "Program.cs" && line.Contains("File.WriteAllText(pidFile, Environment.ProcessId", StringComparison.Ordinal));

    private static List<string> Sites(Regex rule) =>
    [
        .. FakeSources().SelectMany(file => File.ReadLines(file)
            .Select((text, index) => (text, line: index + 1))
            .Where(row => IsCode(row.text) && rule.IsMatch(row.text) && !IsExempt(file, row.text))
            .Select(row => $"{Path.GetFileName(file)}:{row.line}: {row.text.Trim()}")),
    ];

    /// <summary>Every site, joined — the assertion lists them all, not just the first.</summary>
    private static string Listed(List<string> sites) => string.Join(" | ", sites);

    [Fact]
    public void EveryPidFileTheFakeWrites_GoesThroughWritePidFile()
    {
        var sites = Sites(DirectWrite);

        sites.Should().BeEmpty($"a pid file written with File.WriteAllText exists EMPTY for a moment, and a poller that waits for the file reads that (CI run 39760284371): write it through Serving.WritePidFile. Sites: {Listed(sites)}");
    }

    [Fact]
    public void NoWaitForAPidFile_IsAWaitForItsExistenceAlone()
    {
        var sites = Sites(ExistenceWait);

        sites.Should().BeEmpty($"File.Exists on a pid file is true while it is still empty: wait for PidWritten (exists AND holds a pid). Sites: {Listed(sites)}");
    }

    [Fact]
    public void TheScan_SeesWhatItForbids()
    {
        DirectWrite.IsMatch("internal static void WritePid(string file, Process child) => File.WriteAllText(file, child.Id.ToString());").Should().BeTrue();
        DirectWrite.IsMatch("File.WriteAllText(pidFile, Spawned(arguments).Id.ToString());").Should().BeTrue();
        DirectWrite.IsMatch("File.WriteAllLines(Path.Combine(dir, \"x.pid\"), lines);").Should().BeTrue("the target names a .pid path");
        DirectWrite.IsMatch("Serving.WritePidFile(pidFile, [child.Id.ToString()]);").Should().BeFalse("the sanctioned writer");
        DirectWrite.IsMatch("File.WriteAllText(path + \".cwd\", Directory.GetCurrentDirectory());").Should().BeFalse("not a pid file");
        ExistenceWait.IsMatch("_ = Waited(() => File.Exists(Path.Combine(dir, \"late.pid\")));").Should().BeTrue();
        ExistenceWait.IsMatch("while (!File.Exists(trigger))").Should().BeFalse("a trigger is not a pid file");
        ExistenceWait.IsMatch("_ = Waited(() => PidWritten(Path.Combine(dir, \"late.pid\")));").Should().BeFalse("the sanctioned wait");
    }
}
