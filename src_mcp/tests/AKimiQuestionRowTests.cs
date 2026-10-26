using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its roots — never a
/// root — and ends with nothing behind it: its scratch and its one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>) are gone junction-first on an answer,
/// on a refusal before the launch and on a cancellation mid-turn (E5.3 decision g).
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the shim played (<see cref="PlayedKimi"/>) over a fake person
/// (<see cref="KimiFixture"/>) — the shape of <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>. No turn is paid for; the real <c>kimi</c> is never started.
/// Every folder is a <c>testsToDelete-</c> folder of the test's own, and the fake person lives OUTSIDE the homes root.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-qrow-homes-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string OneShotRoot => Path.Combine(_homes, VibeHomes.OneShotFolder);

    public async ValueTask InitializeAsync() =>
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
        _homes.Dispose();
        _person.Dispose();

        return ValueTask.CompletedTask;
    }

    private async Task RepositoryAsync(string at, params (string Path, string Text)[] files)
    {
        Directory.CreateDirectory(at);
        foreach (var (path, text) in files)
        {
            var full = Path.Combine([at, .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, text, TestContext.Current.CancellationToken);
        }

        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await _launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, as the service scenarios do.</summary>
    private IDisposable StandIn() => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>Several scopes, given back in reverse.</summary>
    private sealed class Scopes(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose()
        {
            foreach (var scope in scopes.Reverse())
            {
                scope.Dispose();
            }
        }
    }

    /// <summary>The service with ONE Kimi disk row, the homes root a test folder, the roots granted.</summary>
    private PanelService Service(PlayedKimi played, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = _homes,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "moonshot", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        played,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;

    // ---------- S5.6.7 ----------

    /// <summary>The three endings of a row: answered; refused before the launch (a root outside any Git work tree); cancelled mid-turn.</summary>
    public enum Ending
    {
        Answered,
        RefusedBeforeTheLaunch,
        CancelledMidTurn,
    }

    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.RefusedBeforeTheLaunch)]
    [InlineData(Ending.CancelledMidTurn)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var stood = StandIn();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var before = _person.Hash();

        var root = Path.Combine(Repo, "src");
        if (ending == Ending.RefusedBeforeTheLaunch)
        {
            root = _dir.At("plain");
            Directory.CreateDirectory(root);
        }

        var played = new PlayedKimi(_launcher)
        {
            Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 20000, 30, Advice),
            During = async (_, ct) =>
            {
                if (ending == Ending.CancelledMidTurn)
                {
                    await cts.CancelAsync();
                    await Task.Delay(Timeout.Infinite, ct);
                }
            },
        };

        // A cancelled row may come back as a row status or as OperationCanceledException.
        JsonElement? reply = null;
        try
        {
            reply = await AskAsync(Service(played, root), cts.Token);
        }
        catch (OperationCanceledException) when (ending == Ending.CancelledMidTurn)
        {
            // accepted: the cancelled row ended by exception
        }

        if (reply is { } answered)
        {
            answered.TryGetProperty("answers", out var answers).Should().BeTrue(answered.ToString());
            var row = answers.EnumerateArray().Should().ContainSingle().Subject;
            var status = row.GetProperty("status").GetString();
            if (ending == Ending.Answered)
            {
                status.Should().Be(RowOutcomes.Answered, answered.ToString());
            }
            else
            {
                status.Should().NotBe(RowOutcomes.Answered, answered.ToString());
            }
        }

        string? rowFolder = null;
        if (ending == Ending.RefusedBeforeTheLaunch)
        {
            played.Shims.Should().BeEmpty();
        }
        else
        {
            var request = played.Shims.Should().ContainSingle().Subject.Request;
            rowFolder = Path.GetDirectoryName(request.WorkingDirectory);
            request.StdIn.Should().Contain(Path.Combine(request.WorkingDirectory, "snapshot-"), "the prompt names the copy, never the root");
            request.StdIn.Should().NotContain(Path.Combine(Repo, "src"), "the prompt never names the granted root");
        }

        if (rowFolder is not null)
        {
            Directory.Exists(rowFolder).Should().BeFalse("the row's folder is gone once the row ends");
        }

        (!Directory.Exists(OneShotRoot) || !Directory.EnumerateFileSystemEntries(OneShotRoot).Any()).Should().BeTrue("the one-shot home is gone");
        _unlinkedByTheDeleter.Should().BeEmpty();
        _person.Hash().Should().Equal(before);
    }
}
