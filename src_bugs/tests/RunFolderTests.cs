using System.Reflection;
using CoaiMcp.Tests;
using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// This suite's run folder, and the refusal of a run that cannot have one (E5 S3, <c>todo/PLAN_test_leftovers_E5.md</c>). The library
/// (<c>src_mcp/tests_leftovers</c>) is shared; what is proven HERE is this suite's own wiring: its <c>RunTempStart.cs</c> made, claimed and registered the folder
/// before any test, and its <c>TestEntryPoint.cs</c> is the gated <c>Main</c> (exit 78, one stderr line, before discovery).
/// </summary>
/// <remarks>
/// <para><b>This file is the suite's proof file</b> (S6 marks it for the <c>raw-temp</c> baseline): the TEMP assertion and the forced-failure child runs sit together —
/// a folder that cannot be made, a claim that cannot be written, and EACH of TEMP, TMP and TMPDIR changed alone. Each child exits 78 before discovery.</para>
/// <para>A child gets a filter that matches nothing unless it names the sentinel class, so a gate that fails to refuse ends with "zero tests ran" (exit 8): a clean red.</para>
/// </remarks>
public sealed class RunFolderTests : IDisposable
{
    private const string SentinelVariable = "COAI_S3_SENTINEL";

    private static readonly TimeSpan ChildTimeout = TimeSpan.FromMinutes(3);

    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    private static Dictionary<string, string?> AllThreeAt(string path) => RunTempFolder.Variables.ToDictionary(name => name, _ => (string?)path);

    private static string[] LinesOf(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Exit 78, no result line on stdout, ONE stderr line starting with the prefix and holding <paramref name="mentions"/>, nothing left in the root.</summary>
    private static void TheOneLineRefusal(SuiteChild.Result result, string root, string mentions)
    {
        result.TimedOut.Should().BeFalse("the child must end by itself: " + result.StdErr);
        result.ExitCode.Should().Be(RunTemp.RefusedExitCode, $"stdout: {result.StdOut} / stderr: {result.StdErr}");
        LinesOf(result.StdErr).Should().ContainSingle().Which.Should().StartWith(RunTemp.RefusalPrefix).And.Contain(mentions);
        result.StdOut.Should().NotContain("total:", "no test ran");
        Directory.GetFileSystemEntries(root).Where(entry => !entry.EndsWith("missing", StringComparison.Ordinal)).Should().BeEmpty("the refused child left nothing in its root");
    }

    [Fact]
    public void TheRunFolder_IsTheOwnerForm_AndAllThreeVariablesNameIt()
    {
        RunTemp.Refusal().Should().BeNull("a run that reached a test passed the gate");
        RunTemp.IsActive.Should().BeTrue("Gate checked and passed this run");
        Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Should().Be(RunTemp.Folder);
        Path.GetDirectoryName(RunTemp.Folder).Should().Be(RunTemp.Real, "the run folder is INSIDE the machine's temp folder, never it");
        Path.GetFileName(RunTemp.Folder).Should().MatchRegex("^" + System.Text.RegularExpressions.Regex.Escape(Owned.NameBase) + "run-[A-Za-z0-9]{6}$");
        RunTempFolder.Variables.Select(Environment.GetEnvironmentVariable).Should().OnlyContain(value => value == RunTemp.Folder, "TEMP, TMP and TMPDIR all name the run folder");
    }

    [Fact]
    public void TheEntryPoint_IsTheGatedOne() =>
        Assembly.GetEntryAssembly()!.EntryPoint!.DeclaringType!.FullName.Should().Be("CoaiBugs.Tests.TestEntryPoint", "xUnit's generated Main is behind RunTemp.Gate");

    [Fact]
    public void ARunThatCannotMakeItsFolder_FailsBeforeAnyTest()
    {
        var root = _owned.Dir("s3-nofolder");
        var missing = Path.Combine(root, "missing");

        var result = SuiteChild.Run(SuiteChild.MatchesNothing, AllThreeAt(missing), ChildTimeout);

        TheOneLineRefusal(result, root, missing);
    }

    [Fact]
    public void AClaimThatCannotBeWritten_FailsTheRun()
    {
        var root = _owned.Dir("s3-claim");
        var sentinel = Path.Combine(root, "sentinel");
        var env = AllThreeAt(root);
        env[RunTemp.SelfTestVariable] = "fail-claim";
        env[SentinelVariable] = sentinel;

        var result = SuiteChild.Run(["--filter-class", "*DiscoverySentinel"], env, ChildTimeout);

        TheOneLineRefusal(result, root, "could not claim");
        File.Exists(sentinel).Should().BeFalse("discovery never ran: the refusal is before it");
    }

    /// <summary>Each of the three variables, changed ALONE after the folder was made, is refused on its own: one combined case would pass for a check of only one.</summary>
    [Theory]
    [InlineData("mismatch-temp", "TEMP")]
    [InlineData("mismatch-tmp", "TMP")]
    [InlineData("mismatch-tmpdir", "TMPDIR")]
    public void ARunWhoseVariableNoLongerNamesItsFolder_IsRefused_ForEachVariableAlone(string selfTest, string variable)
    {
        var root = _owned.Dir("s3-lost");
        var env = AllThreeAt(root);
        env[RunTemp.SelfTestVariable] = selfTest;

        var result = SuiteChild.Run(SuiteChild.MatchesNothing, env, ChildTimeout);

        TheOneLineRefusal(result, root, "TEMP, TMP and TMPDIR must all name this run's folder");
        LinesOf(result.StdErr).Single().Should().Contain($"{variable}='").And.Contain("-mismatch'", $"only {variable} was changed");
    }
}

/// <summary>A theory whose <c>MemberData</c> runs at DISCOVERY: with the sentinel variable set it appends a line, so a refused child proves discovery never ran. Skips in a normal run.</summary>
public sealed class DiscoverySentinel
{
    public static TheoryData<int> Rows()
    {
        Append("discovery");

        return new TheoryData<int> { 1 };
    }

    private static void Append(string what)
    {
        if (Environment.GetEnvironmentVariable("COAI_S3_SENTINEL") is { Length: > 0 } file)
        {
            File.AppendAllText(file, what + "\n");
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void ReadsTheSentinel_OnlyWhenAChildIsAskedTo(int row)
    {
        if (Environment.GetEnvironmentVariable("COAI_S3_SENTINEL") is not { Length: > 0 })
        {
            Assert.Skip("child-only: RunFolderTests starts the suite on this class with COAI_S3_SENTINEL set");
        }

        Append("test body");
        row.Should().Be(1);
    }
}
