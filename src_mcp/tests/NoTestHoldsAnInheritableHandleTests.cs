using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// No test ever holds an INHERITABLE handle in the test host (E4.2 decision 10, review B1 and B2 (b) of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): .NET's non-shell <c>Process.Start</c> passes <c>bInheritHandles = TRUE</c> and no handle
/// list, so for as long as a test holds one, every process the other seven parallel tests start inherits it — a sign-in tree hold
/// that outlives its test, held by a stranger's <c>git</c>. The tree hold is taken by the SHIM, which a test plays as a
/// <c>FakeCli.exe tree-hold</c> child; a test re-takes the same files only with the NON-inheriting <c>HeldFile</c> calls.
/// </summary>
/// <remarks>
/// A source scan in the <c>TempDirsAreSwept</c> shape. The fake CLI project is outside <c>src_mcp/tests</c> and so exempt — it is
/// the child that holds. Comments are not code: a doc line may name the call it forbids.
/// </remarks>
public sealed class NoTestHoldsAnInheritableHandleTests
{
    /// <summary>
    /// <c>HoldTree(</c>; the named argument <c>inheritable: true</c>; <c>FileShare.Inheritable</c>; and the same request spelled POSITIONALLY —
    /// <c>Take(path, true)</c> / <c>TakeWithin(path, within, true)</c>, a literal <c>true</c> as the last argument, with at most one level of
    /// call among the arguments (<c>TakeWithin(Path.Combine(a, b), within, true)</c>). A <c>true</c> that closes some other call is not matched.
    /// </summary>
    private static readonly Regex Forbidden = new(
        @"\bHoldTree\s*\(|inheritable\s*:\s*true|FileShare\.Inheritable|\b(?:TakeWithin|Take)\s*\((?:[^()]|\([^()]*\))*,\s*true\s*\)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static IEnumerable<string> TestSources()
    {
        var tests = Path.Combine(RepoPaths.Root(), "src_mcp", "tests");

        return Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => Path.GetFileName(file) != nameof(NoTestHoldsAnInheritableHandleTests) + ".cs");
    }

    /// <summary>Every code line (not a comment) of the test sources that takes or asks for an inheritable hold: <c>file:line: text</c>.</summary>
    private static List<string> Offenders() =>
    [
        .. TestSources().SelectMany(file => File.ReadLines(file)
            .Select((line, index) => (line: line.Trim(), at: index + 1))
            .Where(row => !row.line.StartsWith("//", StringComparison.Ordinal) && Forbidden.IsMatch(row.line))
            .Select(row => $"{Path.GetFileName(file)}:{row.at}: {row.line}")),
    ];

    [Fact]
    public void NoTestFile_CallsHoldTree_OrPassesInheritableTrue()
    {
        TestSources().Select(Path.GetFileName).Should().Contain(["KimiCredentialsLockTests.cs", "KimiHomeTests.cs"], "the scan sees the files that touch the locks");

        Offenders().Should().BeEmpty("only the fake CLI child — the shim — holds an inheritable handle; a test holding one lends it to every process the parallel tests start");
    }

    [Fact]
    public void TheScan_SeesWhatItForbids()
    {
        Forbidden.IsMatch("var again = KimiCredentialsLock.HoldTree(Persons, home, TimeSpan.FromSeconds(1));").Should().BeTrue();
        Forbidden.IsMatch("HeldFile.Take(path, inheritable: true);").Should().BeTrue();
        Forbidden.IsMatch("new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Inheritable)").Should().BeTrue();
        Forbidden.IsMatch("var held = HeldFile.TakeWithin(p, w, true);").Should().BeTrue("a positional true is the same request as inheritable: true");
        Forbidden.IsMatch("HeldFile.Take(path, true);").Should().BeTrue();
        Forbidden.IsMatch("HeldFile.TakeWithin(Path.Combine(home, name), TimeSpan.FromSeconds(1), true)").Should().BeTrue("a nested call among the arguments does not hide it");
        Forbidden.IsMatch("HeldFile.TakeWithin(path, TimeSpan.FromSeconds(1));").Should().BeFalse("the non-inheriting take is what a test uses");
        Forbidden.IsMatch("HeldFile.Take(path);").Should().BeFalse();
        Forbidden.IsMatch("HeldFile.TakeWithin(path, within, false);").Should().BeFalse("an explicit false is not a request to inherit");
        Forbidden.IsMatch("hold.Should().Be(true); var x = Take(a); Check(b, true);").Should().BeFalse("a true that belongs to another call is not this one");
    }
}
