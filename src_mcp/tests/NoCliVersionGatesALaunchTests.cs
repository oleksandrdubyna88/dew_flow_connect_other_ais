using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The owner's decision of 2026-11-26 (ledger 1.8, SPEC C2 item 2), kept as a scan: NO release, version, hash or pin of a vendor's CLI refuses a launch, stops a job or asks for a new measure. Every place of the product that parses a release or tells a person to update a CLI is listed here
/// with the reason it is not a version gate. A new place fails this test: it records the version instead, or it is listed here with its reason (s7 t1, the scan the task asks for, "with a companion that proves it matches something").
/// </summary>
/// <remarks>
/// <para><b>What the scan looks for</b> (code and string literals, no comments): a release parsed to be compared (<c>Version.Parse</c>, <c>Version.TryParse</c>, <c>new Version(</c>), the words of a floor (<c>VersionFloor</c>, <c>MinimumRelease</c>, <c>RequiredRelease</c>…), the cure of a floor (<c>or newer</c>, <c>update claude</c>, <c>update codex</c>,
/// <c>update the CLI</c>) and the reader of the coder's release (<c>JudgeVersion</c>).</para>
/// <para><b>What is not a version gate:</b> a flag or a value the CLI itself rejects (the help does not declare it; the CLI answers with its own error, and coai only chooses the argv or says what the CLI said); a record of the installed release; a version of something that is not a vendor's CLI (coai's own assembly, systemd, the bugs service).</para>
/// <para><b>The companions:</b> the pattern matches a made-up gate of every shape it is written for, and does not match the same words in a comment; every listed file is found by the scan, so the list cannot go stale.</para>
/// </remarks>
public sealed class NoCliVersionGatesALaunchTests
{
    private static readonly Regex Scan = new(
        @"Version\.(?:Try)?Parse\(|new\s+Version\s*\(|\bor newer\b|\bupdate (?:claude|codex|agy|the CLI)\b|JudgeVersion|VersionFloor|MinimumRelease|MinRelease|RequiredRelease|ReleaseFloor",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Every production file the scan finds, and why what it finds there decides no launch, no stop and no refusal by a CLI's version.</summary>
    private static readonly Dictionary<string, string> NotGates = new(StringComparer.Ordinal)
    {
        ["src_mcp/runners/Reviewers/ClaudeWriteCapability.cs"] = "JudgeVersion reads the release for the RECORD only (an older, newer or unreadable one is Ready, the coder's attempt and envelope name it); the one cure sentence left, 'update claude', belongs to a flag the CLI's own --help does not declare, and that is a question of what the CLI does",
        ["src_mcp/runners/Reviewers/CodexTierSupport.cs"] = "reads the release to choose the argv for fast mode Off, because those codex releases reject the value themselves; it says so in a note and refuses nothing",
        ["src_mcp/core/Catalog/FeatureAvailability.cs"] = "the ranges of releases that accept a tier value (what the CLI accepts); the argv is chosen from them, no launch is refused by them",
        ["src_mcp/runners/Reviewers/VendorDiagnosis.cs"] = "the sentence for an error the CLI itself printed after it failed (an unknown value or option); a diagnosis of what happened, never a check before the launch",
        ["src_mcp/src/Server/PanelService.cs"] = "the card's note for what an Off codex row's launches carry on a release that rejects the standard tier; informational, the launch goes on",
        ["src_mcp/runners/Detach/DetachedLauncher.cs"] = "systemd 254 for --expand-environment=no, not a vendor's CLI: a systemd-run that does not know the option refuses with its own error, and nothing runs without the option",
        ["src_mcp/src/Program.cs"] = "coai's own assembly version, not a vendor's CLI",
        ["src_mcp/src/Collecting/UploadRun.cs"] = "the bugs service's release in a sentence about comments of a batch, not a vendor's CLI",
    };

    [Fact]
    public void EveryPlaceThatParsesAReleaseOrTellsAPersonToUpdateACli_IsListedWithItsReason()
    {
        var found = ProductionSources.Files().Where(file => Scan.IsMatch(ProductionSources.SpellingOf(file))).Order(StringComparer.Ordinal).ToList();

        found.Should().Equal(NotGates.Keys.Order(StringComparer.Ordinal),
            "a new place that parses a release or says 'update the CLI' may be a version gate; it records the version instead (the owner's decision of 2026-11-26) or it is listed here with the reason it is not one");
    }

    [Fact]
    public void ACoderOrAReviewerOrAConsultantLaunch_NeverNamesAFloor()
    {
        var floorWords = new Regex(@"VersionFloor|MinimumRelease|MinRelease|RequiredRelease|ReleaseFloor|\bFloorRelease\b", RegexOptions.CultureInvariant);

        ProductionSources.Files().Where(file => floorWords.IsMatch(ProductionSources.SpellingOf(file))).Should().BeEmpty("no floor of a CLI's release exists in the product: the floor of ClaudeWriteCapability became history in the table");
    }

    // ---------- the companions: the pattern matches something ----------

    [Theory]
    [InlineData("if (Version.Parse(installed) < new Version(2, 1, 330)) { return Refused(\"x\"); }")]
    [InlineData("return Version.TryParse(text, out var release) && release >= floor ? Ready : Refused;")]
    [InlineData("var cure = \"update claude, then start the coder again\";")]
    [InlineData("var cure = \"update codex to 0.131 or newer\";")]
    [InlineData("var floor = MinimumRelease;")]
    [InlineData("var say = \"update the CLI, then run it once by hand.\";")]
    [InlineData("var judged = ClaudeWriteCapability.JudgeVersion(text);")]
    public void TheScan_MatchesAGateOfEveryShapeItIsWrittenFor(string gate) =>
        Scan.IsMatch(ProductionSources.JoinedWithText(gate)).Should().BeTrue($"the scan must see this: {gate}");

    [Fact]
    public void TheScan_DoesNotCountTheSameWordsInAComment()
    {
        const string source = "// if (Version.Parse(installed) < new Version(2, 1, 330)) update claude or newer\n/// JudgeVersion MinimumRelease\nvar nothing = 1;\n";

        Scan.IsMatch(ProductionSources.JoinedWithText(source)).Should().BeFalse("a file that merely talks about a floor is not one");
    }

    [Fact]
    public void EveryListedFile_ExistsAndIsFoundByTheScan_SoTheListCannotGoStale()
    {
        foreach (var file in NotGates.Keys)
        {
            ProductionSources.Files().Should().Contain(file, "the listed file is a production file");
            Scan.IsMatch(ProductionSources.SpellingOf(file)).Should().BeTrue($"{file} is listed, so the scan must find something in it");
        }
    }
}
