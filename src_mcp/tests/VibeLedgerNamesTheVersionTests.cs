using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Vibe row's ledger line names the release that ran it (research/RESULTS_vibe_confinement.md §26: the live round's line
/// had an empty <c>cliVersion</c>). The launch control ASKS <c>vibe --version</c> before every launch, so the version is one
/// "already asked" in <c>LaunchOptions.CliVersion</c>'s sense — written as <c>vibe 2.26.1</c>, never Vibe's raw line, which
/// starts with the interpreter's path under the person's profile.
/// </summary>
public sealed class VibeLedgerNamesTheVersionTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-answers-vibeledger-");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task AVibeLaunch_PutsTheVersionItsControlAskedOnTheLedgerLine()
    {
        var work = Directory.CreateDirectory(_dir.At("work")).FullName;
        var vibe = new PlayedVibe(new ProcessLauncher(), """{"findings": []}""");
        var invocation = new VibeRuntime("vibe", HostKind.Windows).Build(RoleCatalog.ArchitectureRole, "review this", work, _dir.At("s.json"), _dir,
            new ReviewerSettings("vibe") { ExecutablePath = PlayedVibe.Exe, Model = "glm-5-3", OwnedScratch = _dir.At("round"), Timeout = TimeSpan.FromSeconds(60) });

        var outcome = await new ReviewerExecutor(vibe).RunAsync(invocation, ct: TestContext.Current.CancellationToken);
        var ledger = new UsageLedger(_dir.At("data"));
        ledger.Record(invocation, outcome, "glm-5-3", "CodeReview", TimeSpan.FromSeconds(1));

        outcome.Should().BeOfType<ReviewerOutcome.Ok>();
        var line = JsonDocument.Parse(File.ReadLines(ledger.Path).Last()).RootElement;
        line.GetProperty("cliVersion").GetString().Should().Be("vibe 2.26.1",
            "the version the launch control asked, named without the interpreter path Vibe prints first");
    }
}
