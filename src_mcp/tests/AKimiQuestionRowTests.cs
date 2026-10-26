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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads COPIES of its granted roots
/// (the prompt names the copies' paths, never the roots), on a ONE-SHOT home under <c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>, and after the row ends — answered,
/// refused before any launch, or cancelled mid-turn — its scratch and its home are gone, junction-first, with the person's folder whole.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the shim played in process (<see cref="PlayedKimi"/>) over a fake person's
/// sign-in (<see cref="KimiFixture"/>, a real junction on Windows, a symbolic link elsewhere through <see cref="WindowsStandIn"/>). No test starts a
/// real <c>kimi</c>. The shape is <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>; the junction-first teeth are <c>AOneShotKimiHomeIsDisposedJunctionFirstTests</c>'s.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => _dir.At("homes");

    /// <summary>How a row's turn ends in S5.6.7's Theory.</summary>
    public enum Ending
    {
        Answered,
        RefusedBeforeTheLaunch,
        CancelledMidTurn,
    }

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Homes);
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"));
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _person.Dispose();
        _dir.Dispose();

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

    /// <summary>The service with the homes root and one <c>disk</c> Kimi row — <see cref="PlayedKimi"/> is its launcher; the fake person is NOT yet standing in (see <see cref="StandIn"/>).</summary>
    private PanelService ServiceOn(PlayedKimi played, params string[] roots)
    {
        return new PanelService(
            new PanelSettings
            {
                ConsultantHomesRoot = Homes,
                Providers = [],
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
                DataDir = _dir.At("data"),
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                QuestionConsult = new QuestionConsultSettings
                {
                    Rows = [new QuestionRow("kimi-disk", "my-kimi", "kimi", string.Empty, string.Empty, string.Empty, string.Empty, "question-disk", Enabled: true)],
                    Roots = roots,
                    RowBudget = TimeSpan.FromSeconds(30),
                },
            },
            VaultKeys.None("no vault in tests"),
            default,
            played,
            Logger.None,
            Noticing.None);
    }

    /// <summary>The fake person's sign-in, the measured host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call so it reaches every awaited service call.</summary>
    private IDisposable StandIn()
    {
        return new Scopes(
            WindowsStandIn.Begin(),
            KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks")),
            KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, []))));
    }

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.RefusedBeforeTheLaunch)]
    [InlineData(Ending.CancelledMidTurn)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        using var stood = StandIn();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var before = _person.Hash();
        string granted = Path.Combine(Repo, "src");
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        string plain = _dir.At("plain");
        if (ending == Ending.RefusedBeforeTheLaunch)
        {
            Directory.CreateDirectory(plain);
            await File.WriteAllTextAsync(Path.Combine(plain, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
        }

        string[] roots = ending == Ending.RefusedBeforeTheLaunch ? new[] { plain } : new[] { granted };

        Func<ProcessRequest, CancellationToken, Task> during = ending == Ending.CancelledMidTurn
            ? async (_, ct) => { await caller.CancelAsync(); ct.ThrowIfCancellationRequested(); }
            : (_, _) => Task.CompletedTask;
        var played = new PlayedKimi(_launcher)
        {
            Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, Advice),
            During = during,
        };

        JsonElement reply = default;
        if (ending == Ending.CancelledMidTurn)
        {
            var asking = async () => await AskAsync(ServiceOn(played, roots), caller.Token);
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            reply = await AskAsync(ServiceOn(played, roots), caller.Token);
        }

        if (ending == Ending.RefusedBeforeTheLaunch)
        {
            var row = TheRow(reply);
            row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused, reply.ToString());
            row.GetProperty("reason").GetString().Should().Contain("not inside a Git work tree");
            played.Shims.Should().BeEmpty("nothing is launched for a row whose copy was refused");
        }
        else
        {
            var launch = played.Shims.Should().ContainSingle("the shim is launched once").Subject;
            var args = launch.Request.Arguments;
            var rowFolder = Path.GetDirectoryName(args[args.ToList().IndexOf("--home") + 1])!;

            if (ending == Ending.Answered)
            {
                TheRow(reply).GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
                launch.Request.StdIn.Should().Contain(launch.Request.WorkingDirectory, "the prompt names the copies, which stand in the row's tree");
                launch.Request.StdIn.Should().NotContain(granted, "never the original root");
            }

            Directory.Exists(rowFolder).Should().BeFalse("the row folder goes with its one-shot home");
            Directory.Exists(Path.GetDirectoryName(launch.Request.WorkingDirectory)).Should().BeFalse("the row's scratch, with its copies, is gone");
        }

        var oneShot = Path.Combine(Homes, VibeHomes.OneShotFolder);
        if (Directory.Exists(oneShot))
        {
            Directory.EnumerateFileSystemEntries(oneShot).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        }

        _unlinkedByTheDeleter.Should().BeEmpty();
        _person.Hash().Should().Equal(before);
    }
}
