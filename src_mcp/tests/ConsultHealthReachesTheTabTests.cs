using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultation's outcome reaches <c>consultations/health/consultants.json</c> the moment it is recorded — the file the
/// OTHER side's window reads — without a re-probe, and the notice a failed turn leaves names its kind.
/// </summary>
/// <remarks>
/// The whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03), findings A1 and D. Before it,
/// a failure was written to the caller kind's health file only, and <c>consultants.json</c> — rewritten by a survey
/// alone — went on telling a plain Windows window that the WSL consultant was healthy until the next server start.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ConsultHealthReachesTheTabTests : ConsultScenarioBase
{
    private string ConsultantsFile => Path.Combine(_data, "consultations", "health", "consultants.json");

    /// <summary>A survey as a server would have written it before the turn — with facts only a probe knows.</summary>
    private void Surveyed()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConsultantsFile)!);
        File.WriteAllText(ConsultantsFile, """
            {
              "utc": "2026-10-01T08:00:00.0000000Z",
              "side": "windows",
              "aFieldANewerBuildWrote": "kept",
              "consultants": [
                { "callerKind": "claude", "available": true, "vendor": "codex", "runtime": "codex", "model": "",
                  "cli": { "probed": true, "found": true, "version": "probed-before-the-turn", "authSource": "own auth", "note": "" },
                  "lastAnswer": null, "lastFailure": null, "failureCurrent": false },
                { "callerKind": "codex", "available": true, "vendor": "claude", "runtime": "claude", "model": "",
                  "lastFailure": { "utc": "2026-09-30T08:00:00.0000000Z", "kind": "quota", "vendor": "claude" }, "failureCurrent": true }
              ]
            }
            """);
    }

    private JsonNode Row(string kind) =>
        JsonNode.Parse(File.ReadAllText(ConsultantsFile))!["consultants"]!.AsArray().Single(row => (string?)row!["callerKind"] == kind)!;

    private static void Fails()
    {
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", null);
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "error: unknown option '--restricted'");
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "1");
    }

    [Fact]
    public async Task AFailedTurn_RewritesItsRowInConsultantsJson_AndLeavesEverythingElseAsItWas()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        Surveyed();
        Fails();

        await Consult(Service(), "why is the count wrong");

        var row = Row(CallerIdentity.Claude);
        ((string?)row["lastFailure"]?["kind"]).Should().Be("vendor-refused", "the failure reaches the file the other side reads, at once");
        ((bool?)row["failureCurrent"]).Should().BeTrue();
        ((string?)row["cli"]?["version"]).Should().Be("probed-before-the-turn", "nothing is re-probed: only the outcome fields move");
        var root = JsonNode.Parse(File.ReadAllText(ConsultantsFile))!;
        ((string?)root["utc"]).Should().Be("2026-10-01T08:00:00.0000000Z", "the survey's time is the time its probes were taken");
        ((string?)root["aFieldANewerBuildWrote"]).Should().Be("kept");
        ((string?)Row(CallerIdentity.Codex)["lastFailure"]?["kind"]).Should().Be("quota", "another caller kind's row is not this turn's");
    }

    [Fact]
    public async Task AnAnsweredTurnAfterAFailure_ClearsTheRowsCurrentFailure()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        Surveyed();
        var service = Service();
        Fails();
        await Consult(service, "why is the count wrong");
        Answer("0198-second", Advice);

        await Consult(service, "why is the count wrong, asked again");

        var row = Row(CallerIdentity.Claude);
        ((bool?)row["failureCurrent"]).Should().BeFalse("the same consultant answered since");
        ((string?)row["lastAnswer"]?["vendor"]).Should().Be("codex");
        ((string?)row["lastFailure"]?["kind"]).Should().Be("vendor-refused", "nothing is deleted — the reader decides");
    }

    [Fact]
    public async Task WithNoConsultantsFile_AFailureWritesNone()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        Fails();

        await Consult(Service(), "why is the count wrong");

        File.Exists(ConsultantsFile).Should().BeFalse("only a survey makes the file; an outcome only refreshes one that is there");
    }

    [Fact]
    public async Task AFailedTurnsNotice_IsKeyedByItsKind_NotByTheMethodThatAppliedIt()
    {
        // The extension keys repeats on (code, subject): with the subject the compiler filled in — "Applied" for every
        // failure — a quota and a denied shell command collapsed into one row a person cannot read.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        List<ServerNotice> offered = [];
        Fails();

        await Consult(Service(noticing: new Noticing(notice => { offered.Add(notice); return true; }, Serilog.Core.Logger.None)), "why is the count wrong");

        offered.Select(notice => notice.Subject).Should().Contain("consult:vendor-refused").And.NotContain("Applied");
    }
}
