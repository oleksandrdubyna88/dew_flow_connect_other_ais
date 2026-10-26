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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads a COPY of its granted
/// roots, is told the copies' paths and never the originals, and the row's one-shot home (<c>&lt;root&gt;\coai-vh\&lt;8&gt;</c>) and its scratch are
/// gone once the row ends — on an answer, a refusal before the launch and a cancellation alike — each disposed junction-first.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the Kimi shim played in process (<see cref="PlayedKimi"/>) over a fake
/// person (<see cref="KimiFixture"/>) whose folder lives outside everything the row deletes. The model is
/// <c>AVibeQuestionRowReadsACopyOfItsRootsTests</c> (same harness, the Kimi seams instead of the Vibe path budget).
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => _dir.At("homes");

    public async ValueTask InitializeAsync() =>
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"));

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

    private PanelService Service(PlayedKimi played, params string[] roots) => new(
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

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    /// <summary>
    /// The home the launch was given is <c>&lt;Homes&gt;\coai-vh\&lt;8 hex&gt;\kh-&lt;10 hex&gt;</c> — never TEMP or anywhere else — and that folder is gone,
    /// with the reservation's own <c>&lt;8 hex&gt;</c> folder (the one-shot folder itself may remain, empty).
    /// </summary>
    private void TheOneShotHomeIsGone(PlayedKimi.Launch launch)
    {
        var home = launch.ArgAfter("--home");
        var reservation = Path.GetDirectoryName(home)!;
        var oneShot = Path.Combine(Homes, VibeHomes.OneShotFolder);
        Path.GetDirectoryName(reservation).Should().Be(oneShot, "the row's home is under the homes root's coai-vh, never under TEMP");
        Path.GetFileName(reservation).Should().MatchRegex("^[0-9a-f]{8}$");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        Directory.Exists(home).Should().BeFalse("the row's home is deleted on every way out");
        Directory.Exists(reservation).Should().BeFalse("the reservation's own folder is given back on every way out");
        if (Directory.Exists(oneShot))
        {
            Directory.EnumerateFileSystemEntries(oneShot).Should().BeEmpty("nothing is left under coai-vh");
        }
    }

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var stood = KimiStandIn.Begin(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        Directory.CreateDirectory(Homes);
        var granted = Path.Combine(Repo, "src");
        if (ending == "refused")
        {
            granted = Directory.CreateDirectory(_dir.At("plain")).FullName;
            await File.WriteAllTextAsync(Path.Combine(granted, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
        }

        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var copies = new List<string>();
        var played = new PlayedKimi(_launcher)
        {
            // The copies exist only while the row runs: what the shim could read is taken at the turn.
            Turn = (_, request) =>
            {
                copies.AddRange(Directory.EnumerateDirectories(request.WorkingDirectory).Where(copy => File.Exists(Path.Combine(copy, "Mailer.cs"))));

                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, Advice);
            },
            During = async (_, ct) =>
            {
                if (ending == "cancelled")
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };

        if (ending == "cancelled")
        {
            var asking = async () => await AskAsync(Service(played, granted), caller.Token);

            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var reply = await AskAsync(Service(played, granted), TestContext.Current.CancellationToken);

            TheRowEnded(ending, reply, played, granted, copies);
        }

        played.Shims.Should().HaveCount(ending == "refused" ? 0 : 1);
        foreach (var launch in played.Shims)
        {
            Directory.Exists(launch.Request.WorkingDirectory).Should().BeFalse("the row's copies are gone once it ended");
            TheOneShotHomeIsGone(launch);
        }

        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder is whole: names, bytes and ReadOnly bits");
    }

    private static void TheRowEnded(string ending, JsonElement reply, PlayedKimi played, string granted, List<string> copies)
    {
        var row = TheRow(reply);
        if (ending == "refused")
        {
            row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused, reply.ToString());
            row.GetProperty("reason").GetString().Should().Contain("not inside a Git work tree");

            return;
        }

        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
        var request = played.Shims.Should().ContainSingle().Subject.Request;
        var copy = copies.Should().ContainSingle("the one granted root is copied once, and its file is there to read").Subject;
        request.StdIn.Should().Contain(copy, "the prompt names the copy Kimi can read");
        request.StdIn.Should().NotContain(granted, "never the original root");
        request.Arguments.Should().NotContain("--session", "a question row is one shot");
    }
}
