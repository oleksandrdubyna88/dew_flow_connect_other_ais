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
/// A source scan in the <c>NoTestHoldsAnInheritableHandleTests</c> shape over <c>src_mcp/tests_fakecli/*.cs</c>. Each file is matched WHOLE (a call split over
/// several lines is found — the convention "if the pattern spans lines, match across lines"; the checkpoint-1 finding F0). Comments are not code. A statement
/// naming an <c>.eof</c> marker is not a pid file. <c>Program.cs</c>'s <c>FAKECLI_VERSION_PID</c> write is exempt on purpose: a <c>--version</c>
/// writes its own pid once and the test reads it AFTER the launcher killed that process — nobody polls it.
/// </remarks>
public sealed class EveryPidFileTheFakeWritesIsWrittenWholeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(1);

    /// <summary>A direct write: the call whose FIRST argument is a pid-named identifier or a <c>.pid</c> path, or any write inside a <c>WritePid…(</c> one-liner.</summary>
    /// <remarks>Matched over the WHOLE source, so a call a formatter splits over several lines is found: whitespace in the pattern is <c>\s</c> (it includes a newline) and "the rest of the statement" is <c>[^;]*?</c>, never <c>.*</c>.</remarks>
    private static readonly Regex DirectWrite = new(@"\bFile\s*\.\s*WriteAll(?:Text|Lines)\s*\(\s*(?:\w*[Pp]id\w*\s*,|[^;]*?\.pid"")|\b[Ww]rite[Pp]id\w*\s*\([^;]*?\bFile\s*\.\s*WriteAll(?:Text|Lines)\s*\(", RegexOptions.CultureInvariant, Bound);

    /// <summary>A wait for a pid file's existence alone: <c>File.Exists(</c> over a <c>.pid</c> path or a pid-named identifier — across lines, and never past the end of the statement or into a block.</summary>
    private static readonly Regex ExistenceWait = new(@"\bFile\s*\.\s*Exists\s*\([^;{}]*?(?:\.pid""|[Pp]id[A-Z]?\w*\s*\))", RegexOptions.CultureInvariant, Bound);

    private static IEnumerable<string> FakeSources() =>
        Directory.EnumerateFiles(Path.Combine(RepoPaths.Root(), "src_mcp", "tests_fakecli"), "*.cs", SearchOption.TopDirectoryOnly);

    private static bool IsCode(string line) => !line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static bool IsExempt(string file, string line) =>
        line.Contains("\".eof\"", StringComparison.Ordinal)
        || (Path.GetFileName(file) == "Serving.cs" && line.Contains("File.WriteAllLines(writing, lines)", StringComparison.Ordinal))
        || (Path.GetFileName(file) == "Program.cs" && line.Contains("File.WriteAllText(pidFile, Environment.ProcessId", StringComparison.Ordinal));

    private static List<string> Sites(Regex rule) =>
        Sites(rule, FakeSources().Select(file => (Name: file, Text: File.ReadAllText(file))));

    /// <summary>The sites of one rule in the given sources (a name and a text each) — the seam the scan's own tests give a synthetic source through.</summary>
    internal static List<string> Sites(Regex rule, IEnumerable<(string Name, string Text)> sources) =>
    [
        .. sources.SelectMany(source => SitesIn(rule, source.Name, CodeOnly(source.Text))),
    ];

    /// <summary>The source with its comment lines emptied — emptied, not removed, so a match's line number is still the file's.</summary>
    private static string CodeOnly(string text) => string.Join('\n', text.Split('\n').Select(line => IsCode(line) ? line : string.Empty));

    /// <summary>Every match of the rule over the whole text, as <c>file:line: statement</c>, minus the exempt ones.</summary>
    private static IEnumerable<string> SitesIn(Regex rule, string file, string code) =>
        rule.Matches(code)
            .Select(match => (line: LineOf(code, match.Index), statement: StatementFrom(code, match.Index)))
            .Where(site => !IsExempt(file, site.statement))
            .Select(site => $"{Path.GetFileName(file)}:{site.line}: {site.statement}");

    private static int LineOf(string code, int index) => 1 + code.AsSpan(0, index).Count('\n');

    /// <summary>The statement a match begins, up to its <c>;</c> and with its whitespace (newlines too) collapsed to single spaces — what is reported and what the exemptions are asked about.</summary>
    private static string StatementFrom(string code, int index)
    {
        var end = code.IndexOf(';', index);

        return Regex.Replace(code[index..(end < 0 ? code.Length : end + 1)], @"\s+", " ").Trim();
    }

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

    // The convention: when the pattern spans lines, the scan matches across lines. A call a formatter splits over several lines evades a line-by-line scan.
    private const string SplitWrite = "static void Leak(string pidFile, Process child)\n{\n    File.WriteAllText(\n        pidFile,\n        child.Id.ToString());\n}\n";

    [Fact]
    public void ADirectWriteSplitAcrossLines_IsFound_AtTheLineWhereTheCallBegins()
    {
        var sites = Sites(DirectWrite, [("Split.cs", SplitWrite)]);

        sites.Should().ContainSingle("the call is forbidden however a formatter wraps it").Which.Should().StartWith("Split.cs:3:");
    }

    [Fact]
    public void ADirectWriteWhoseTargetIsOnTheNextLine_IsFound()
    {
        var sites = Sites(DirectWrite, [("Split.cs", "File.WriteAllLines(\n    Path.Combine(dir, \"x.pid\"),\n    lines);\n")]);

        sites.Should().ContainSingle();
    }

    [Fact]
    public void AnExistenceWaitSplitAcrossLines_IsFound()
    {
        var sites = Sites(ExistenceWait, [("Split.cs", "_ = Waited(() => File.Exists(\n    Path.Combine(dir, \"late.pid\")));\n")]);

        sites.Should().ContainSingle();
    }

    [Fact]
    public void ASplitCallThatIsSanctioned_OrCommentedOut_OrNotAPidFile_IsNotFound()
    {
        var innocent = "Serving.WritePidFile(\n    pidFile,\n    [child.Id.ToString()]);\n"
            + "// File.WriteAllText(\n//     pidFile, \"x\");\n"
            + "File.WriteAllText(\n    path + \".cwd\",\n    Directory.GetCurrentDirectory());\n"
            + "while (!File.Exists(\n    trigger))\n{\n    Thread.Sleep(10);\n}\n"
            + "File.WriteAllText(\n    pidFile + \".eof\",\n    \"eof\");\n";

        Sites(DirectWrite, [("Split.cs", innocent)]).Should().BeEmpty();
        Sites(ExistenceWait, [("Split.cs", innocent)]).Should().BeEmpty();
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
