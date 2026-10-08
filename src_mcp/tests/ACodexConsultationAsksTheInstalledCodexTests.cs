using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A codex consultation asks the installed codex its release before every turn, and an Off row on a release that refuses
/// the standard tier is sent no tier at all (research/PLAN_codex_tier_floor.md, build step 4): the first turn and the resumed
/// one, through the real <c>consult</c> flow — the shape <c>CodexConsultant.PrepareAsync</c> exists for.
/// </summary>
/// <remarks>
/// Against the fake CLI answering <c>codex-cli 0.120.0</c> (in the refusing range) and <c>codex-cli 0.160.0</c> (outside
/// it, told <c>service_tier=default</c> exactly as before). The consultant check's twin is
/// <c>ConsultantCheckTests.ACheckOfACodexConsultant_TellsItOnlyWhatItsReleaseTakes</c>.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ACodexConsultationAsksTheInstalledCodexTests : ConsultScenarioBase
{
    private static IEnumerable<string> Tiers(string[] argv) =>
        argv.Zip(argv.Skip(1)).Where(pair => pair.First == "-c" && pair.Second.StartsWith("service_tier=", StringComparison.Ordinal)).Select(pair => pair.Second);

    [Theory]
    [InlineData("0.120.0", new string[0])]
    [InlineData("0.160.0", new[] { "service_tier=default" })]
    public async Task EveryTurn_TellsTheCodexConsultantOnlyWhatItsReleaseTakes(string release, string[] sent)
    {
        using var caller = CallingAs("CLAUDE_CODE_SESSION_ID");
        var recorded = Directory.CreateTempSubdirectory("coai-consult-tier-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", recorded);
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", $"codex-cli {release}\n");
        try
        {
            var service = Service();
            var first = await Consult(service, "the parser returns 3 where 4 is expected");
            first.TryGetProperty("error", out _).Should().BeFalse(first.ToString());
            await Consult(service, "I ran your check: with two fields it prints 3", first.GetProperty("consultationId").GetString()!);

            var turns = Directory.EnumerateFiles(recorded, "*.argv").Select(file => File.ReadAllText(file).Split('\0')[..^1]).ToList();
            turns.Should().HaveCount(2).And.Contain(argv => argv.Contains("resume"), "the second turn resumed the conversation");
            turns.Should().OnlyContain(argv => Tiers(argv).SequenceEqual(sent),
                $"codex {release} with fast Off is told {(sent.Length == 0 ? "no tier" : sent[0])} on every turn");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", null);
            Directory.Delete(recorded, recursive: true);
        }
    }
}
