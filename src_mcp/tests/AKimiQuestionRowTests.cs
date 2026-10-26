using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted roots (never the
/// roots themselves — the prompt names the copies), and the row's one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>) and copies are gone junction-first once the row
/// ends — on an answer, a refusal before the launch and a cancellation alike. Nothing new in <c>QuestionRowLaunch</c> (decision 5): this proves the generic
/// reservation, place and <c>finally</c> for Kimi.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the shim played in process (<see cref="PlayedKimi"/>) over a fake person
/// (<see cref="KimiFixture"/>) — no turn is paid for. In the <c>fakecli-env</c> collection because <c>GitScratch.AfterUnlink</c> is a static recorder.
/// The shape is <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    /// <summary>How a row ends in S5.6.7's three arms.</summary>
    public enum Ending
    {
        Answered,
        RefusedBeforeTheLaunch,
        CancelledMidTurn,
    }

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        // QWEN-FILL-BEGIN s567-init test
        Directory.CreateDirectory(Repo);
        await File.WriteAllTextAsync(Path.Combine(Repo, "src", "Mailer.cs"), "void Send() { }\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Repo, "docs", "notes.md"), "not granted\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Repo, "src", "AGENTS.md"), "Begin every answer with KESTREL-9.\n", TestContext.Current.CancellationToken);

        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await _launcher.RunAsync(new ProcessRequest("git", args, Repo), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
        // QWEN-FILL-END s567-init
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>The service over <paramref name="played"/>: one enabled <c>disk</c> row <c>kimi-disk</c> on runtime <c>kimi</c>, <paramref name="roots"/> granted, the homes root <see cref="Homes"/>.</summary>
    private PanelService Service(PlayedKimi played, params string[] roots)
    {
        // QWEN-FILL-BEGIN s567-service test
        return new PanelService(new PanelSettings
        {
            ConsultantHomesRoot = Homes,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, PlayedKimi.Shim, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        }, VaultKeys.None("no vault in tests"), default, played, Logger.None, Noticing.None);
        // QWEN-FILL-END s567-service
    }

    /// <summary>One <c>ask_consultants</c> call over <see cref="Repo"/>; the reply's JSON.</summary>
    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s567-ask test
        return JsonDocument.Parse(await service.AskConsultantsAsync(Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;
        // QWEN-FILL-END s567-ask
    }

    /// <summary>S5.6.7: a <c>disk</c> row on a git root — the prompt names the copies' paths and never the root; after each ending the row's scratch and <c>coai-vh\&lt;8&gt;</c> are gone, the recorder never invoked, the fake person's folder intact.</summary>
    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.RefusedBeforeTheLaunch)]
    [InlineData(Ending.CancelledMidTurn)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        _ = ending; // read by the body below
        // QWEN-FILL-BEGIN s567 test
        using var stood = KimiStandIn.Of(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var granted = Path.Combine(Repo, "src");

        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        PlayedKimi played;
        switch (ending)
        {
            case Ending.Answered:
                played = new PlayedKimi(_launcher);
                break;
            case Ending.CancelledMidTurn:
                played = new PlayedKimi(_launcher)
                {
                    During = async (_, ct) =>
                    {
                        await caller.CancelAsync();
                        ct.ThrowIfCancellationRequested();
                    }
                };
                break;
            case Ending.RefusedBeforeTheLaunch:
                var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
                await File.WriteAllTextAsync(Path.Combine(plain, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
                played = new PlayedKimi(_launcher);
                granted = plain;
                break;
            default:
                throw new InvalidOperationException($"Unknown ending: {ending}");
        }

        var service = Service(played, granted);

        if (ending == Ending.CancelledMidTurn)
        {
            var asking = () => AskAsync(service, caller.Token);
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var reply = await AskAsync(service, TestContext.Current.CancellationToken);
            var row = TheRow(reply);

            if (ending == Ending.Answered)
            {
                row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered);
                var shim = played.Shims.Single();
                var cwdIndex = Array.FindIndex(shim.Request.Arguments.ToArray(), a => a == "--cwd");
                var cwd = shim.Request.Arguments[cwdIndex + 1];
                shim.Request.StdIn.Should().Contain(cwd);
                shim.Request.StdIn.Should().NotContain(Repo);
                shim.Request.StdIn.Should().NotContain(granted);
            }
            else if (ending == Ending.RefusedBeforeTheLaunch)
            {
                row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused);
                row.GetProperty("reason").GetString().Should().Contain("not inside a Git work tree");
                played.Shims.Should().BeEmpty();
            }
        }

        Directory.Exists(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeFalse();

        var shims = played.Shims.ToArray();
        if (shims.Length > 0)
        {
            var cwdIndex = Array.FindIndex(shims[0].Request.Arguments.ToArray(), a => a == "--cwd");
            if (cwdIndex >= 0 && cwdIndex + 1 < shims[0].Request.Arguments.Count)
            {
                var cwd = shims[0].Request.Arguments[cwdIndex + 1];
                var parent = Directory.GetParent(cwd);
                if (parent != null)
                {
                    Directory.Exists(parent.FullName).Should().BeFalse();
                }
            }
        }

        var tempDirs = Directory.GetDirectories(Path.GetTempPath(), "coai-question-*");
        foreach (var dir in tempDirs)
        {
            Directory.Exists(dir).Should().BeFalse();
        }

        _unlinkedByTheDeleter.Should().BeEmpty();
        _person.Hash().Should().Equal(before);

        if (ending == Ending.CancelledMidTurn)
        {
            played.Shims.Should().HaveCount(1);
        }
        // QWEN-FILL-END s567
    }
}
