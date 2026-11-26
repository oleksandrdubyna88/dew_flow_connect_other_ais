using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s6 t1 (V6, the owner's decision of 2026-11-26): an agy coder is launched on whatever release is installed. What it asks is what the INSTALLED CLI declares in its help — <c>--mode</c> with <c>accept-edits</c>, <c>--agent</c>, <c>--add-dir</c>, <c>--conversation</c> and the
/// stream formats — and a flag that is not declared is a refusal that names the flag, never a release. The release is a record: named beside the last measured one, and nothing compares the two.
/// </summary>
/// <remarks>
/// The fixture is the REAL usage text of agy as it printed it for an unknown flag (<c>fixtures/role-stops/agy-bad-flag.json</c>, recorded on the measuring machine); the broken variants are that text with one line taken out, so each refusal is tested against a help that is otherwise acceptable.
/// No real agy is started: the launcher is a stand-in.
/// </remarks>
public sealed class AAgyWriteCapabilityTests
{
    private const string Printed = "1.5.18\n";

    private static string Help()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "role-stops", "agy-bad-flag.json")));
        var stderr = fixture.RootElement.GetProperty("stderr").GetString()!;

        return stderr[stderr.IndexOf("Usage of agy:", StringComparison.Ordinal)..];
    }

    private static string WithoutLine(string help, string flag) =>
        string.Join('\n', help.Split('\n').Where(line => !line.TrimStart().StartsWith(flag + " ", StringComparison.Ordinal)));

    private static AgyWriteCapability.Refused Refusal(string version, string help) =>
        AgyWriteCapability.Judge(version, help).Should().BeOfType<AgyWriteCapability.Refused>().Subject;

    // ---------- d1: what the help declares ----------

    [Fact]
    public void TheFixture_IsTheRealHelp_AndDeclaresEveryFlagOfTheArgv()
    {
        Help().Should().Contain("--mode").And.Contain("accept-edits").And.Contain("--conversation", "the fixture must be agy's own usage text");
        AgyWriteCapability.Judge(Printed, Help()).Should().Be(new AgyWriteCapability.Ready("1.5.18"));
        AgyWriteCapability.LastMeasuredRelease.Should().Be("1.5.18", "the release the rows were measured on is history, named beside the installed one");
    }

    [Theory]
    [InlineData("--mode")]
    [InlineData("--agent")]
    [InlineData("--add-dir")]
    [InlineData("--conversation")]
    [InlineData("--input-format")]
    [InlineData("--output-format")]
    [InlineData("--print")]
    [InlineData("--model")]
    public void AFlagThatIsNotDeclared_IsARefusal_ThatNamesTheFlag_AndNeverTheRelease(string flag)
    {
        var refusal = Refusal(Printed, WithoutLine(Help(), flag));

        refusal.Said.Should().Contain(flag);
        refusal.Said.Should().NotContain("1.5.18", "a refusal names the property, never the version");
        refusal.Cure.Should().NotContain("1.5.18");
        refusal.CouldNotStart.Should().BeFalse();
    }

    [Fact]
    public void AModeThatDoesNotListAcceptEdits_IsARefusal_ThatSaysSo()
    {
        var help = Help().Replace("(accept-edits, auto, plan)", "(auto, plan)", StringComparison.Ordinal);
        help.Should().NotBe(Help());

        Refusal(Printed, help).Said.Should().Contain("--mode").And.Contain("accept-edits");
    }

    [Fact]
    public void AStreamFormatThatIsNotListed_IsARefusal_ThatNamesTheFlag()
    {
        var help = string.Join('\n', Help().Split('\n').Select(line => line.TrimStart().StartsWith("--input-format ", StringComparison.Ordinal) ? "  --input-format   Input format for print mode (text, ndjson)" : line));
        help.Should().NotBe(Help());

        Refusal(Printed, help).Said.Should().Contain("--input-format").And.Contain("stream-json");
    }

    [Fact]
    public void AWordInProse_IsNotADeclaration()
    {
        // The flag appears only inside another option's description: it is a word in prose, not an option of the CLI.
        var help = WithoutLine(Help(), "--agent") + "\n  --effort-note   use --agent for another agent (prose, not an option)\n";

        Refusal(Printed, help).Said.Should().Contain("--agent");
    }

    [Fact]
    public void AColouredHelp_IsReadThroughItsColour()
    {
        var coloured = System.Text.RegularExpressions.Regex.Replace(Help(), "(?m)^(  (?:-\\w, )?)(--[a-z-]+)", "$1\u001b[36m$2\u001b[39m");
        coloured.Should().NotBe(Help());

        AgyWriteCapability.Judge(Printed, coloured).Should().BeOfType<AgyWriteCapability.Ready>();
    }

    // ---------- d2: the release is a record ----------

    [Theory]
    [InlineData("1.5.19")]
    [InlineData("1.5.17")]
    [InlineData("1.6.0")]
    [InlineData("0.9.1")]
    [InlineData("agy 1.5.18")]
    [InlineData("1.5.19 (build 77)")]
    public void ANewerAnOlderAndAReleaseWithWordsAround_IsReady_AsPrinted(string printed)
    {
        var judged = AgyWriteCapability.Judge(printed + "\n", Help());

        var ready = judged.Should().BeOfType<AgyWriteCapability.Ready>().Subject;
        ready.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$").And.Be(System.Text.RegularExpressions.Regex.Match(printed, @"\d+\.\d+\.\d+").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("a banner and no number\n")]
    [InlineData("dev-build\n")]
    public void ANameTheCliDoesNotGive_IsRecordedAsUnknown_AndLaunched(string printed) =>
        AgyWriteCapability.Judge(printed, Help()).Should().Be(new AgyWriteCapability.Ready(AgyWriteCapability.UnknownRelease));

    [Fact]
    public void NoSentenceOfTheCapability_AsksForANewMeasureANewJudgementOrAnUpdate()
    {
        // Every refusal the capability can make, read as text: it names a flag or a state of the CLI, and asks for no measure, no judgement, no pin and no update of the program.
        var sentences = new List<string>();
        foreach (var flag in new[] { "--mode", "--agent", "--add-dir", "--conversation", "--input-format", "--output-format", "--print", "--model" })
        {
            var refusal = Refusal(Printed, WithoutLine(Help(), flag));
            sentences.Add(refusal.Said);
            sentences.Add(refusal.Cure);
        }

        var all = string.Join('\n', sentences).ToLowerInvariant();
        foreach (var word in new[] { "measure", "judgement", "judgment", "pin", "newer", "older", "update", "upgrade", "release", "version" })
        {
            all.Should().NotContainEquivalentOf(word, $"no refusal of the agy capability may talk of '{word}': the release gates nothing");
        }
    }

    // ---------- the probe ----------

    [Fact]
    public async Task TheProbe_AsksTheVersionAndTheHelp_ThroughTheOneLauncher_AndNothingElse()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => new ProcessResult(0, Printed, string.Empty, false),
            ["--help"] => new ProcessResult(0, string.Empty, Help(), false),
            _ => throw new InvalidOperationException("a capability check started a turn"),
        });

        var answer = await AgyWriteCapability.ProbeAsync(launcher, "agy", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);

        answer.Should().Be(new AgyWriteCapability.Ready("1.5.18"), "the help of agy may be on standard error, as the usage text of its flag parser is");
        launcher.Requests.Select(request => request.Arguments[0]).Should().Equal("--version", "--help");
        launcher.Requests.Should().OnlyContain(request => request.Timeout <= TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task TheProbe_ReadsAHelpOnStandardOutput_AndOneThatExitsNonZero()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => new ProcessResult(0, Printed, string.Empty, false),
            _ => new ProcessResult(2, Help(), string.Empty, false),
        });

        (await AgyWriteCapability.ProbeAsync(launcher, "agy", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken)).Should().Be(new AgyWriteCapability.Ready("1.5.18"));
    }

    [Fact]
    public async Task AVersionThatDidNotAnswer_IsUnknown_AndTheHelpDecides()
    {
        var launcher = new WatchedLauncher(new ProcessLauncher(), request => request.Arguments switch
        {
            ["--version"] => new ProcessResult(-1, string.Empty, string.Empty, true),
            _ => new ProcessResult(0, Help(), string.Empty, false),
        });

        (await AgyWriteCapability.ProbeAsync(launcher, "agy", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken)).Should().Be(new AgyWriteCapability.Ready(AgyWriteCapability.UnknownRelease));
    }

    [Fact]
    public async Task ACliThatCannotBeStarted_IsARefusalThatSaysSo()
    {
        var answer = await AgyWriteCapability.ProbeAsync(new ProcessLauncher(), Path.Combine(Path.GetFullPath("anywhere"), "no-such-agy"), Path.GetFullPath("."), TestContext.Current.CancellationToken);

        var refusal = answer.Should().BeOfType<AgyWriteCapability.Refused>().Subject;
        refusal.CouldNotStart.Should().BeTrue();
        refusal.Said.Should().Contain("could not be started");
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("empty")]
    [InlineData("failed")]
    public async Task AHelpThatDoesNotAnswer_IsARefusalThatSaysSo_AfterTwoAsks(string how)
    {
        var helpAsks = 0;
        var launcher = new WatchedLauncher(new ProcessLauncher(), request =>
        {
            if (request.Arguments is ["--version"])
            {
                return new ProcessResult(0, Printed, string.Empty, false);
            }

            helpAsks++;

            return how switch
            {
                "timeout" => new ProcessResult(-1, string.Empty, string.Empty, true),
                "empty" => new ProcessResult(0, string.Empty, string.Empty, false),
                _ => new ProcessResult(1, string.Empty, "boom", false),
            };
        });

        var answer = await AgyWriteCapability.ProbeAsync(launcher, "agy", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);

        var refusal = answer.Should().BeOfType<AgyWriteCapability.Refused>().Subject;
        refusal.Said.Should().Contain("--help");
        refusal.CouldNotStart.Should().BeFalse();
        helpAsks.Should().Be(2, "a first start after a self-update can be slow: the help is asked once more before it is a refusal");
    }

    [Fact]
    public async Task AskedOnEveryLaunch_NeverCached()
    {
        var versions = 0;
        var launcher = new WatchedLauncher(new ProcessLauncher(), request =>
        {
            if (request.Arguments is ["--version"])
            {
                versions++;

                return new ProcessResult(0, $"1.5.{18 + versions}\n", string.Empty, false);
            }

            return new ProcessResult(0, Help(), string.Empty, false);
        });

        var first = await AgyWriteCapability.ProbeAsync(launcher, "agy", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);
        var second = await AgyWriteCapability.ProbeAsync(launcher, "agy", Path.GetFullPath("anywhere"), TestContext.Current.CancellationToken);

        (first, second).Should().Be((new AgyWriteCapability.Ready("1.5.19"), new AgyWriteCapability.Ready("1.5.20")), "a self-update between two launches is read again, and refuses nothing");
    }

    // ---------- d1/d4: the table's history names one release; releases around it are launched ----------

    [Theory]
    [InlineData("1.5.19")]
    [InlineData("1.5.17")]
    [InlineData("")]
    public void ATableWhoseHistoryNamesOneRelease_StillLaunchesAReleaseOneAboveOneBelowAndOneTheCliDoesNotName(string installed)
    {
        var table = CoderBoundaryTable.Parse("""
            { "version": 1, "judgement": "j.md", "shared": [],
              "vendors": [ { "id": "agy", "verdict": "ship", "results": "r.md", "launchable": true,
                "measured": [ { "release": "1.5.18", "resultsHeading": "H", "judgementHeading": "J", "programs": [ { "file": "agy", "sha256": "0000000000000000000000000000000000000000000000000000000000000000" } ] } ],
                "properties": [], "pending": [] } ] }
            """);
        var set = CoderVendorSet.Of(table);

        // The table decides by verdict and mark; nothing compares the installed release with its history.
        set.Table.WhyNot("agy").Should().BeEmpty();
        set.LastMeasured("agy").Should().Be("1.5.18");
        AgyWriteCapability.Judge(installed + "\n", Help()).Should().BeOfType<AgyWriteCapability.Ready>();
    }
}
