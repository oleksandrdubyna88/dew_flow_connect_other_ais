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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads a COPY of its granted root and is
/// told the copy's path, never the root's; and its one-shot home and copy are gone junction-first once the row ends — on an answer, a refusal before
/// the launch and a cancellation alike. The whole <c>ask_consultants</c> flow through the service, git for real, the shim played in process
/// (<see cref="PlayedKimi"/>) over a fake person's sign-in (<see cref="KimiFixture"/>).
/// </summary>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string OneShotFolder = "coai-vh";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");
    private readonly List<string> _unlinked = [];
    private readonly List<string> _linked = [];

    private string Repo => _dir.At("repo");

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"),
            ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

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

    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

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

    private PanelService ServiceFor(IProcessLauncher launcher, string homesRoot, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = homesRoot,
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
        launcher,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    private static string After(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    /// <summary>The row's own <c>coai-question-*</c> scratch above a copy's path — the folder that holds the copies of the person's code.</summary>
    private static string ScratchAbove(string copy)
    {
        for (var at = copy; !string.IsNullOrEmpty(at); at = Path.GetDirectoryName(at))
        {
            if (Path.GetFileName(at).StartsWith(QuestionRowLaunch.ScratchPrefix, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return string.Empty;
    }

    private static HashSet<string> QuestionScratches() =>
        [.. Directory.EnumerateDirectories(Path.GetTempPath(), QuestionRowLaunch.ScratchPrefix + "*")];

    private static IEnumerable<string> Entries(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder) : [];

    /// <summary>S5.6.7: answered, refused before the launch, cancelled mid-turn — each leaves no home, no copy and the person's folder whole.</summary>
    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        // Set here, not in InitializeAsync: an async method's change to an AsyncLocal does not reach the test that awaited it.
        GitScratch.AfterUnlink = _unlinked.Add;
        KimiHome.AfterUnlink = _linked.Add;
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var homes = Directory.CreateDirectory(_dir.At("homes")).FullName;
        var before = person.Hash();
        var scratchesBefore = QuestionScratches();
        var granted = Path.Combine(Repo, "src");
        if (ending == "refused")
        {
            granted = Directory.CreateDirectory(_dir.At("plain")).FullName;
            await File.WriteAllTextAsync(Path.Combine(granted, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
        }

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var played = new PlayedKimi(_launcher)
        {
            During = ending == "cancelled"
                ? async (_, ct) =>
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                }
            : static (_, _) => Task.CompletedTask,
        };
        var service = ServiceFor(played, homes, granted);
        using var stop = new CancellationTokenSource();

        var asking = AskAsync(service, ending == "cancelled" ? stop.Token : TestContext.Current.CancellationToken);
        if (ending == "cancelled")
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await stop.CancelAsync();
        }

        JsonElement reply = default;
        try
        {
            reply = await asking;
        }
        catch (OperationCanceledException) when (ending == "cancelled")
        {
            // The row's own record, or the token: what this test pins is what it leaves behind.
        }

        switch (ending)
        {
            case "answered":
                TheRow(reply).GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
                var request = played.Shims.Should().ContainSingle().Subject.Request;
                var copy = After(request, "--cwd");
                request.StdIn.Should().Contain(copy, "the prompt names the copy Kimi can read").And.NotContain(granted, "never the original");
                Directory.Exists(Path.GetDirectoryName(After(request, "--home"))).Should().BeFalse("the row's home and copy are gone once it answered");
                ScratchAbove(copy).Should().NotBeEmpty("the copy stands in the row's own coai-question-* scratch");
                Directory.Exists(ScratchAbove(copy)).Should().BeFalse("the scratch that held the copies of the person's code is removed once the row answered");
                break;
            case "refused":
                TheRow(reply).GetProperty("status").GetString().Should().Be(RowOutcomes.Refused, reply.ToString());
                played.Shims.Should().BeEmpty("nothing of Kimi's starts for a row whose copy was refused");
                QuestionScratches().Except(scratchesBefore).Should().BeEmpty("a refused row leaves no coai-question-* scratch behind");
                break;
            default:
                var cutShort = After(played.Shims.Should().ContainSingle().Subject.Request, "--cwd");
                ScratchAbove(cutShort).Should().NotBeEmpty("the copy stands in the row's own coai-question-* scratch");
                Directory.Exists(ScratchAbove(cutShort)).Should().BeFalse("a cancelled row's scratch is removed too");
                break;
        }

        _linked.Should().HaveCount(ending == "refused" ? 0 : 1, "a one-shot home was made and its credentials junction unlinked once — else the negative checks below test nothing");

        Entries(Path.Combine(homes, OneShotFolder)).Should().BeEmpty("the row's one-shot folder is given back on every way out");
        _unlinked.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }
}
