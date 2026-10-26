using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row on a <c>disk</c> grant (S5.6.7 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>): the whole
/// <c>ask_consultants</c> flow through the service, git for real, the Kimi shim played in process (<see cref="PlayedKimi"/>), and the fake
/// person's sign-in (<see cref="KimiFixture"/>). The row is told the COPIES' paths and never the root; after each ending the row's home and
/// folder are gone, nothing was unlinked through the deleter, and the person's sign-in is whole.
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiDiskRowLeavesNothingBehindTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("coai-qkimi-");

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"));
    }

    public ValueTask DisposeAsync()
    {
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

    /// <summary>The person, the host and the lock root this flow's Kimi adapter sees (the <c>AKimiConsultationScenarioTests</c> precedent).</summary>
    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

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
                Rows = [new QuestionRow("kimi-disk", "kimi", "kimi", KimiModels.Default, string.Empty, PlayedKimi.Shim, string.Empty, "question-disk", Enabled: true)],
                Roots = roots,
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        kimi,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;

    /// <summary>The value after <c>--home</c> on the shim's argv.</summary>
    private static string HomeOf(ProcessRequest request) => request.Arguments.SkipWhile(a => a != "--home").Skip(1).First();

    /// <summary>Every file under a folder, by its path relative to it, with its bytes.</summary>
    private static Dictionary<string, byte[]> Bytes(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file), File.ReadAllBytes);

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var person = new KimiFixture();
        using var scope = StandIn(person);
        using var cancel = new CancellationTokenSource();
        var kimi = new PlayedKimi(_launcher)
        {
            Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, Advice),
            During = (_, ct) => ending == "cancelled" ? CancelThenWait(cancel, ct) : Task.CompletedTask,
        };
        var root = ending == "refused" ? Directory.CreateDirectory(_dir.At("plain")).FullName : Path.Combine(Repo, "src");
        if (ending == "refused")
        {
            await File.WriteAllTextAsync(Path.Combine(root, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
        }

        var before = Bytes(person.PersonsHome);
        var unlinked = new List<string>();
        JsonElement reply;
        GitScratch.AfterUnlink = unlinked.Add;
        try
        {
            reply = await AskAsync(Service(kimi, root), ending == "cancelled" ? cancel.Token : TestContext.Current.CancellationToken);
        }
        catch (OperationCanceledException) when (ending == "cancelled")
        {
            reply = default;
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        if (ending == "refused")
        {
            kimi.Shims.Should().BeEmpty("a row refused at its copy is never launched");
            var row = TheRow(reply);
            row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused, reply.ToString());
            row.GetProperty("reason").GetString().Should().Contain("not inside a Git work tree");
        }

        if (ending == "answered")
        {
            var turn = kimi.Shims.Should().ContainSingle().Subject.Request;
            turn.StdIn.Should().Contain(turn.WorkingDirectory, "the prompt names the copy Kimi can read");
            turn.StdIn.Should().NotContain(Repo, "the prompt never names the root: the row reads its copy (M-K9)");
            TheRow(reply).GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
        }

        if (ending == "cancelled")
        {
            kimi.Shims.Should().ContainSingle("the turn had started when the row was cancelled");
        }

        foreach (var request in kimi.Shims.Select(launch => launch.Request))
        {
            var home = HomeOf(request);
            Directory.Exists(home).Should().BeFalse($"{ending}: the row's home is gone once it ends");
            Directory.Exists(Path.GetDirectoryName(home)!).Should().BeFalse($"{ending}: the row's folder is gone once it ends");
        }

        unlinked.Should().BeEmpty($"{ending}: the home was unlinked junction-first, so the deleter never met it");
        Bytes(person.PersonsHome).Should().BeEquivalentTo(before, $"{ending}: the person's sign-in is never written through the link");
    }

    /// <summary>The played turn cancels the row, then waits for the cancellation to reach it.</summary>
    private static async Task CancelThenWait(CancellationTokenSource cancel, CancellationToken ct)
    {
        await cancel.CancelAsync();
        await Task.Delay(Timeout.Infinite, ct);
    }

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }
}
