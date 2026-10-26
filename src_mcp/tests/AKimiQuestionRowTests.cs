using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
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
/// and is told the copies' paths, never the roots; and once the row ends — answered, refused before the launch, or cancelled mid-turn — its
/// scratch and its one-shot folder <c>coai-vh\&lt;8&gt;</c> are gone, junction-first (E5.3 decision g).
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the real launch control over a fake person's sign-in
/// (<see cref="KimiFixture"/>), only the shim played (<see cref="PlayedKimi"/>) — no turn is paid for and the real <c>kimi</c> never starts.
/// Shaped like <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c>. The person's folder lives outside the homes root the row deletes.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync() =>
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        _dir.Dispose();
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

    /// <summary>The service with one Kimi <c>disk</c> row and the Homes root — the row reserves its one-shot folder under <c>coai-vh</c> there (D1).</summary>
    private PanelService Service(PlayedKimi kimi, params string[] roots) => new(
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
        kimi,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    /// <summary>A folder in no Git work tree — a root the snapshot refuses.</summary>
    private string PlainFolder()
    {
        var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
        File.WriteAllText(Path.Combine(plain, "notes.txt"), "not a repository");

        return plain;
    }

    // ---------- S5.6.7 ----------

    /// <summary>
    /// The three endings of a row — <c>answered</c>, <c>refused</c> (a root outside any Git work tree, before the launch) and <c>cancelled</c> (the
    /// caller's token, mid-turn) — each leave the row's scratch and its <c>coai-vh\&lt;8&gt;</c> folder gone and the fake person's folder whole.
    /// </summary>
    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var stood = AKimiConsultationScenarioTests.StandIn(_person);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(_launcher)
        {
            During = ending == "cancelled" ? async (_, ct) => { await caller.CancelAsync(); ct.ThrowIfCancellationRequested(); } : (_, _) => Task.CompletedTask,
        };
        var granted = ending == "refused" ? PlainFolder() : Path.Combine(Repo, "src");
        var before = _person.Hash();
        RecordingTheDeleter();

        if (ending == "cancelled")
        {
            var asking = async () => await AskAsync(Service(played, granted), caller.Token);
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var reply = await AskAsync(Service(played, granted));
            TheRow(reply).GetProperty("status").GetString().Should().Be(ending == "answered" ? RowOutcomes.Answered : RowOutcomes.Refused, reply.ToString());
        }

        if (ending == "refused")
        {
            played.Shims.Should().BeEmpty("a root outside any Git work tree is refused before anything launches");
        }
        else
        {
            var launch = played.Shims.Should().ContainSingle().Subject;
            launch.CredentialsLinked.Should().BeTrue("the row's home held its credentials link during the turn, so an empty deleter list means the home took it away");
            var turn = launch.Request;
            turn.StdIn.Should().Contain(turn.WorkingDirectory, "the prompt names the copy Kimi reads");
            turn.StdIn.Should().NotContain(granted, "never the root");
            Directory.Exists(turn.WorkingDirectory).Should().BeFalse("the row's scratch is gone");
        }

        Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        _unlinkedByTheDeleter.Should().BeEmpty("the home's junction was unlinked by KimiHome, never met by the deleter");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }
}
