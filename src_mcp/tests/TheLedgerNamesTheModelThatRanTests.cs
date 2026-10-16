using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Serilog;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ledger names the model that RAN, not the one the row said (decision 75 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): the first live round with a Kimi row
/// wrote an empty <c>model</c> on every line while the reviewer states carried <c>kimi-code/k3-256k</c> — the row's model was empty, the adapter resolved its default
/// into the invocation, and the engine handed the ledger the ROW's. Vibe resolves a default the same way and had the same gap; every other adapter puts the row's own
/// model into the invocation, so the two were never different for them.
/// </summary>
public sealed class TheLedgerNamesTheModelThatRanTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-ledger-model-");

    public void Dispose() => _dir.Dispose();

    private static ReviewerOutcome Answered() => new ReviewerOutcome.Ok(new NormalisedReview([], []), false, new Usage(10, 2, null));

    private string RecordedModel(ReviewerInvocation invocation, string rowsModel)
    {
        var ledger = new UsageLedger(_dir.At("data"));
        ledger.Record(invocation, Answered(), rowsModel, "CodeReview", TimeSpan.FromSeconds(1));

        return JsonDocument.Parse(File.ReadLines(ledger.Path).Last()).RootElement.GetProperty("model").GetString()!;
    }

    [Fact]
    public void AKimiRowWithNoModel_IsLedgeredOnTheDefaultItRanOn()
    {
        var runtime = new KimiRuntime("kimi", HostKind.Windows, new KimiSeams(root => KimiPlaces.ForShim(root, _dir.At("bin")), () => (_dir.At("coai-mcp.exe"), [])));
        var invocation = runtime.Build(RoleCatalog.ArchitectureRole, "review", _dir.At("work"), _dir.At("s.json"), _dir,
            new ReviewerSettings("kimi") { Model = string.Empty, OwnedScratch = _dir.At("round"), Timeout = TimeSpan.FromMinutes(1) });

        invocation.Model.Should().Be(KimiModels.Default, "the fixture: the adapter resolved the default");
        RecordedModel(invocation, rowsModel: string.Empty).Should().Be("kimi-code/k3-256k");
    }

    [Fact]
    public void AVibeRowWithNoModel_IsLedgeredOnTheDefaultItRanOn_TheSameGap()
    {
        var invocation = new VibeRuntime("vibe", HostKind.Windows, VibePathBudget.None).Build(RoleCatalog.ArchitectureRole, "review", _dir.At("work"), _dir.At("s.json"), _dir,
            new ReviewerSettings("vibe") { Model = string.Empty, OwnedScratch = _dir.At("round"), Timeout = TimeSpan.FromMinutes(1) });

        invocation.Model.Should().Be(VibeModels.Default);
        RecordedModel(invocation, rowsModel: string.Empty).Should().Be(VibeModels.Default);
    }

    [Fact]
    public void ARowThatNamesItsModel_IsLedgeredOnIt_AndAnAdapterThatNamesNone_FallsBackToTheRows()
    {
        RecordedModel(new ReviewerInvocation("codex", "r", new Runners.Processes.ProcessRequest("codex", [], ".")) with { Model = "gpt-x" }, rowsModel: "gpt-x").Should().Be("gpt-x");
        RecordedModel(new ReviewerInvocation("codex", "r", new Runners.Processes.ProcessRequest("codex", [], ".")), rowsModel: "from-the-row").Should().Be("from-the-row");
        RecordedModel(new ReviewerInvocation("codex", "r", new Runners.Processes.ProcessRequest("codex", [], ".")), rowsModel: string.Empty).Should().BeEmpty("a CLI that runs on its own default says nothing, and the ledger does not invent one");
    }
}
