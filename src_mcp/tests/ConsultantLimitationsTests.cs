using CoaiMcp.Runners.Platform;
using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a consultant can be kept from reading, per platform — and every sentence says where it was measured, or
/// that it was not.
/// </summary>
/// <remarks>
/// <para>E3.3 and requirements 12–13 of PLAN_the_consultant_works_on_every_vendor.md. The rows are
/// <c>shared/consultant-limitations.json</c>, embedded in the server; these tests read the repository's copy as the
/// source of truth and hold the embedded one to it.</para>
/// <para>The lists these iterate — the consulting runtimes, the platforms — are read from the code
/// (<see cref="ConsultantResolution.Consulting"/>, <see cref="HostKind"/>), never retyped here, so a runtime or a
/// platform added later is a red test until its rows exist (testing.md, <i>A test that repeats a list the code also
/// holds will not notice the third entry</i>).</para>
/// </remarks>
public sealed class ConsultantLimitationsTests
{
    private static IReadOnlyList<ConsultantLimitation> Repository() =>
        ConsultantLimitations.Parse(SharedFixtures.Text("consultant-limitations.json"));

    /// <summary>Every platform a row may name — the enum's members, minus the one that means "none of these".</summary>
    private static IReadOnlyList<HostKind> Platforms() => [.. Enum.GetValues<HostKind>().Where(host => host != HostKind.Other)];

    private static readonly ClaudeCapability[] Capabilities = [ClaudeCapability.WithRestricted, ClaudeCapability.NoRestricted];

    // ---------- the file ----------

    [Fact]
    public void TheFile_ParsesThroughTheSourceGeneratedContext_AndTheServerCarriesThatFile()
    {
        var rows = Repository();

        rows.Should().NotBeEmpty("a file that did not load holds no rows, and every assertion below would pass over nothing");
        ConsultantLimitations.All.Should().BeEquivalentTo(rows, options => options.WithStrictOrdering(),
            "the embedded resource is the repository's file — a published binary has no shared/ directory to read");
    }

    [Fact]
    public void EveryRow_IsMeasured_OrSaysWhyNot()
    {
        foreach (var row in Repository())
        {
            var why = $"{row.Runtime}/{HostKinds.Word(row.Platform)}/{row.Capability}";
            switch (row.Evidence)
            {
                case LimitationEvidence.Measured measured:
                    new[] { measured.Date, measured.CliVersion, measured.Cells, measured.Document }.Should().OnlyContain(field => field.Length > 0, why);
                    break;
                case LimitationEvidence.Sourced sourced:
                    sourced.Sentence.Should().NotBeNullOrWhiteSpace(why);
                    break;
                default:
                    throw new InvalidOperationException($"{why}: evidence of an unknown kind");
            }
        }
    }

    [Fact]
    public void ARowWithNoEvidence_OrBoth_IsABrokenFile_NotAQuietRow()
    {
        const string neither = """{"rows":[{"runtime":"codex","platform":"linux","standing":"unconfined","text":"reads anywhere"}]}""";
        const string both = """
            {"rows":[{"runtime":"codex","platform":"linux","standing":"unconfined","text":"reads anywhere","source":"upstream says so",
              "measured":{"date":"2026-10-01","cliVersion":"codex-cli 0.156.1","cells":"3 of 3","document":"research/X.md"}}]}
            """;
        const string halfMeasured = """{"rows":[{"runtime":"codex","platform":"linux","standing":"unconfined","text":"x","measured":{"date":"2026-10-01"}}]}""";

        FluentActions.Invoking(() => ConsultantLimitations.Parse(neither)).Should().Throw<InvalidOperationException>().WithMessage("*neither*");
        FluentActions.Invoking(() => ConsultantLimitations.Parse(both)).Should().Throw<InvalidOperationException>().WithMessage("*both*");
        FluentActions.Invoking(() => ConsultantLimitations.Parse(halfMeasured)).Should().Throw<InvalidOperationException>().WithMessage("*neither*");
    }

    [Fact]
    public void AnUnknownPlatformOrStanding_IsRefused_NamingWhatIsLegal()
    {
        FluentActions.Invoking(() => ConsultantLimitations.Parse("""{"rows":[{"runtime":"codex","platform":"any","standing":"confined","text":"x","source":"y"}]}"""))
            .Should().Throw<InvalidOperationException>().WithMessage("*'any'*windows, linux, wsl, macos*");
        FluentActions.Invoking(() => ConsultantLimitations.Parse("""{"rows":[{"runtime":"codex","platform":"linux","standing":"safe","text":"x","source":"y"}]}"""))
            .Should().Throw<InvalidOperationException>().WithMessage("*'safe'*confined, unconfined, default-deny, unmeasured*");
    }

    [Fact]
    public void EveryConsultingRuntime_HasItsOwnRowOnEveryPlatform_AndClaudeOnePerCapability()
    {
        var rows = Repository();

        foreach (var runtime in ConsultantResolution.Consulting)
        {
            foreach (var platform in Platforms())
            {
                var qualifiers = runtime == "claude" ? Capabilities.Select(c => c.Qualifier).ToArray() : [string.Empty];
                foreach (var qualifier in qualifiers)
                {
                    rows.Count(row => row.Runtime == runtime && row.Platform == platform && row.Capability == qualifier).Should().Be(1,
                        $"{runtime} on {HostKinds.Word(platform)} ({qualifier}) needs exactly one row of its own — an explicit 'unmeasured' where nothing was run");
                }
            }
        }
    }

    // ---------- the lookup ----------

    [Fact]
    public void NoPlatformInheritsAnothersRow()
    {
        foreach (var runtime in ConsultantResolution.Consulting)
        {
            foreach (var host in Platforms())
            {
                foreach (var claude in Capabilities)
                {
                    ConsultantLimitations.Lookup(host, runtime, claude).Platform.Should().Be(host,
                        $"{runtime} on {HostKinds.Word(host)} is answered by a row about {HostKinds.Word(host)}");
                }
            }
        }

        ConsultantLimitations.Lookup(HostKind.Linux, "codex", ClaudeCapability.NoRestricted).Text
            .Should().NotBe(ConsultantLimitations.Lookup(HostKind.Windows, "codex", ClaudeCapability.NoRestricted).Text,
                "codex on Linux is documented upstream and on Windows measured here — two different claims");
    }

    [Fact]
    public void AnUnknownPlatform_OrRuntime_IsUnmeasured_NeverTheNearestRow()
    {
        foreach (var runtime in (string[])[.. ConsultantResolution.Consulting, "gemini"])
        {
            var row = ConsultantLimitations.Lookup(HostKind.Other, runtime, ClaudeCapability.WithRestricted);

            row.Standing.Should().Be(LimitationStanding.Unmeasured, $"{runtime} on a platform nobody has run");
            row.Platform.Should().Be(HostKind.Other);
            row.Evidence.Should().BeOfType<LimitationEvidence.Sourced>().Which.Sentence.Should().Contain("not evidence");
        }

        ConsultantLimitations.Lookup(HostKind.Windows, "gemini", ClaudeCapability.NoRestricted).Standing
            .Should().Be(LimitationStanding.Unmeasured, "a runtime the file does not describe is not described");
    }

    [Fact]
    public void AClaudeWithoutRestricted_OnWindows_GetsTheNotConfinedRow_NotTheMeasuredOne()
    {
        // Epic 3's plan round: the 9 of 9 the Windows row cites were measured WITH --restricted. A Windows claude
        // that lacks the flag is launched without it, and must not be shown that row.
        var without = ConsultantLimitations.Lookup(HostKind.Windows, "claude", ClaudeCapability.NoRestricted);
        var with = ConsultantLimitations.Lookup(HostKind.Windows, "claude", ClaudeCapability.WithRestricted);

        without.Standing.Should().Be(LimitationStanding.Unconfined);
        without.Text.Should().Contain("cannot be confined to the repository").And.Contain("Update claude");
        with.Standing.Should().Be(LimitationStanding.Confined);
        with.Evidence.Should().BeOfType<LimitationEvidence.Measured>().Which.Should().BeEquivalentTo(new
        {
            Date = "2026-10-02",
            CliVersion = "claude 2.1.258",
            Document = "research/RESULTS_claude_consultant_confinement.md",
        });
    }

    [Fact]
    public void TheConfinedClaudeRow_IsWindowsOnly_BecauseThatIsWhereItWasMeasured_AndSaysOnWhat()
    {
        var confined = Repository().Where(row => row.Runtime == "claude" && row.Standing == LimitationStanding.Confined).ToList();

        confined.Select(row => row.Platform).Should().Equal([HostKind.Windows],
            "--restricted was measured on Windows 2.1.258 alone; elsewhere the same flags are sent and not claimed");
        confined[0].Text.Should().Contain("measured on claude 2.1.258 with its default model",
            "the probe ran the default model only; another model is not settled (RESULTS_claude_consultant_confinement.md)");
    }

    [Fact]
    public void TheWindowsClaudeWithoutRestricted_IsAnInference_NotAMeasurementOfThatArgv()
    {
        // The 2026-10-01 leak was claude 2.1.258 with --tools and no --restricted — a claude that HAS the flag, run
        // without it. A Windows claude that genuinely lacks the flag was never measured; the row reasons from the leak.
        var row = ConsultantLimitations.Lookup(HostKind.Windows, "claude", "no-restricted");

        row.Evidence.Should().BeOfType<LimitationEvidence.Sourced>().Which.Sentence.Should().Contain("2.1.258");
    }

    // ---------- the lookup takes a word ----------

    [Fact]
    public void TheLookupTakesTheQualifierWord_AndEveryOtherRuntimeNeedsNone()
    {
        ConsultantLimitations.Lookup(HostKind.Windows, "claude", "no-restricted").Standing.Should().Be(LimitationStanding.Unconfined);
        ConsultantLimitations.Lookup(HostKind.Windows, "claude", "restricted").Standing.Should().Be(LimitationStanding.Confined);
        ConsultantLimitations.Lookup(HostKind.Windows, "codex").Standing.Should().Be(LimitationStanding.Unconfined,
            "codex's rows carry no capability, so no claude value has to be invented for it");
        ConsultantLimitations.Lookup(HostKind.Windows, "claude").Standing.Should().Be(LimitationStanding.Unmeasured,
            "a claude with no recorded or probed capability selects no row of its own");
        ConsultantLimitations.Lookup(HostKind.Windows, "claude", ClaudeCapability.Unknown("claude --help exited 1")).Standing
            .Should().Be(LimitationStanding.Unmeasured, "a capability nobody could tell claims nothing");
    }

    // ---------- antigravity's allow rule ----------

    [Fact]
    public void TheAllowRuleSnippet_IsOnlyOnTheLinuxAndWslAntigravityRows()
    {
        var withSnippet = Repository().Where(row => row.Snippet.Length > 0).ToList();

        withSnippet.Select(row => (row.Runtime, row.Platform)).Should().BeEquivalentTo(
            [("antigravity", HostKind.Linux), ("antigravity", HostKind.Wsl)],
            "a prefix rule ran 2 of 2 in WSL and was refused 2 of 2 on Windows (research/RESULTS_agy_allow_rule.md)");
        foreach (var row in withSnippet)
        {
            using var snippet = JsonDocument.Parse(row.Snippet);
            snippet.RootElement.GetProperty("permissions").GetProperty("allow")[0].GetString().Should().Be("command(git grep)");
            row.Text.Should().Contain("not read-only", "a prefix rule lets `git grep -O<cmd>` run a program");
        }
    }

    /// <summary>
    /// Requirement 13: the snippet never travels without its warning — <c>--consultants</c> hands both to the panel
    /// as fields, so the warning a person reads beside a copy button is a field of the row, not prose to mine.
    /// </summary>
    [Fact]
    public void EverySnippet_CarriesItsWarning_AndTheLoaderRefusesOneWithout_OrOffLinux()
    {
        foreach (var row in Repository().Where(row => row.Snippet.Length > 0))
        {
            row.SnippetWarning.Should().Contain("not read-only", $"{row.Runtime} on {row.Platform} offers a rule to paste");
        }

        const string snippet = "\"snippet\":\"{\\\"permissions\\\":{\\\"allow\\\":[\\\"command(git grep)\\\"]}}\"";
        FluentActions.Invoking(() => ConsultantLimitations.Parse(
                $$"""{"rows":[{"runtime":"antigravity","platform":"wsl","standing":"default-deny","text":"x","source":"y",{{snippet}}}]}"""))
            .Should().Throw<InvalidOperationException>().WithMessage("*snippetWarning*");
        FluentActions.Invoking(() => ConsultantLimitations.Parse(
                $$"""{"rows":[{"runtime":"antigravity","platform":"windows","standing":"default-deny","text":"x","source":"y",{{snippet}},"snippetWarning":"not read-only"}]}"""))
            .Should().Throw<InvalidOperationException>().WithMessage("*linux*wsl*");
    }

    [Fact]
    public void TheAgySettingsFile_IsNamedForEachSide()
    {
        var rows = Repository().Where(row => row.Runtime == "antigravity").ToDictionary(row => row.Platform);

        rows[HostKind.Windows].SettingsPath.Should().Be(@"%USERPROFILE%\.gemini\antigravity-cli\settings.json");
        rows[HostKind.Linux].SettingsPath.Should().Be("~/.gemini/antigravity-cli/settings.json");
        rows[HostKind.Wsl].SettingsPath.Should().Be("~/.gemini/antigravity-cli/settings.json");
        rows[HostKind.Windows].Text.Should().Contain("only exact command lines");
    }

    // ---------- the evidence is real ----------

    [Fact]
    public void EveryMeasuredDocumentOnThisBranch_Exists()
    {
        // A citation is only evidence if it can be opened. A document on another branch says so in the row
        // ("… on branch feat/question-consultant") and is exempt here; everything else must be in this tree.
        var root = Path.GetDirectoryName(Path.GetDirectoryName(SharedFixtures.PathOf("consultant-limitations.json")))!;
        var cited = Repository()
            .Select(row => row.Evidence).OfType<LimitationEvidence.Measured>()
            .SelectMany(measured => measured.Document.Split(", "))
            .Where(document => !document.Contains(" on branch ", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        cited.Should().NotBeEmpty("the measured rows cite documents in this repository");
        cited.Should().OnlyContain(document => File.Exists(Path.Combine(root, document)), "a cited document must exist");
    }
}

/// <summary>Which platform this is — WSL told apart from Linux, and an unknown one left unknown.</summary>
public sealed class HostKindTests
{
    [Theory]
    [InlineData(true, false, false, false, HostKind.Windows, "windows")]
    [InlineData(false, true, false, false, HostKind.Linux, "linux")]
    [InlineData(false, true, false, true, HostKind.Wsl, "wsl")]
    [InlineData(false, false, true, false, HostKind.MacOs, "macos")]
    [InlineData(false, false, false, false, HostKind.Other, "other")]
    public void TheFourFacts_NameTheHost(bool windows, bool linux, bool macOs, bool wsl, HostKind expected, string word)
    {
        HostKinds.Of(windows, linux, macOs, wsl).Should().Be(expected);
        HostKinds.Word(expected).Should().Be(word);
    }
}
