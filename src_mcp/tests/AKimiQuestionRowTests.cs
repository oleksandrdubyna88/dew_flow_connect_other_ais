using System.Collections.Concurrent;
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
/// A Kimi question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): a <c>disk</c> row reads a COPY of its
/// operator's granted root — and is told the copy's path, never the root's — and the row's home, its one-shot folder and its copy are
/// gone once the row ends, on an answer, a refusal and a cancellation alike, junction-first, the fake person's sign-in whole.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the real launch control over a fake person
/// (<see cref="KimiFixture"/>, a real junction on Windows, a symbolic link elsewhere through <see cref="WindowsStandIn"/>), and only the
/// shim played (<see cref="PlayedKimi"/>): no turn is paid for and the real <c>kimi</c> is never started.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("coai-qkimi-");
    private readonly TempDir _homesDir = TempDir.For("testsToDelete-kimi-question-");

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_homesDir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Repo);
        await File.WriteAllTextAsync(Path.Combine(Repo, "Mailer.cs"), "void Send() { }\n", TestContext.Current.CancellationToken);
        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await _launcher.RunAsync(new ProcessRequest("git", args, Repo), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    public ValueTask DisposeAsync()
    {
        _homesDir.Dispose();
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

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

    /// <summary>
    /// The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, so the values reach the test's own flow
    /// and every service call it awaits (copied from <c>AKimiConsultationScenarioTests</c>).
    /// </summary>
    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

    private PanelService Service(PlayedKimi kimi, string homesRoot, params string[] roots) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = homesRoot,
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
        },
        VaultKeys.None("no vault in tests"),
        default,
        kimi,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null) =>
        JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;

    private string PlainFolder()
    {
        var plain = Directory.CreateDirectory(_dir.At("plain")).FullName;
        File.WriteAllText(Path.Combine(plain, "notes.txt"), "not a repository");

        return plain;
    }

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    /// <summary>The row's own folder — the parent of the working folder its launch was given, where its copies were made.</summary>
    private static string RowFolderOf(ProcessRequest request) => Path.GetDirectoryName(CwdOf(request))!;

    /// <summary>The working folder a launch was given: the copies of the row's roots are its sub-folders.</summary>
    private static string CwdOf(ProcessRequest request) => request.Arguments[request.Arguments.ToList().IndexOf("--cwd") + 1];

    /// <summary>The row's one-shot folder is gone on every way out: nothing is left under its homes root's coai-vh.</summary>
    private void TheHomesFolderIsEmpty() =>
        Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's coai-vh folder is gone on every way out");

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        var before = person.Hash();
        var recorded = new ConcurrentQueue<string>();
        var homeRecorded = new ConcurrentQueue<string>();
        var copies = new List<string>();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        // A folder outside any Git work tree: its copy is refused before anything launches (the row's own refusal, as Vibe's class does).
        var granted = ending == "refused" ? PlainFolder() : Repo;
        var played = new PlayedKimi(_launcher)
        {
            // The copies exist only while the row runs, so they are read here, in the launch.
            Turn = (_, request) =>
            {
                copies.AddRange(Directory.GetDirectories(CwdOf(request)));

                return PlayedKimi.Answered(PlayedKimi.SessionOf(request), 1000, 10, Advice);
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

        GitScratch.AfterUnlink = recorded.Enqueue;
        KimiHome.AfterUnlink = homeRecorded.Enqueue;
        try
        {
            var service = Service(played, Homes, granted);
            var asking = async () => await AskAsync(service, caller.Token);

            if (ending == "cancelled")
            {
                await asking.Should().ThrowAsync<OperationCanceledException>();
            }
            else
            {
                var reply = await AskAsync(service);
                var row = TheRow(reply);

                if (ending == "answered")
                {
                    row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
                }
                else
                {
                    row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused, reply.ToString());
                    row.GetProperty("reason").GetString().Should().Contain("not inside a Git work tree", reply.ToString());
                }
            }
        }
        finally
        {
            GitScratch.AfterUnlink = null;
            KimiHome.AfterUnlink = null;
        }

        if (ending == "answered")
        {
            var shim = played.Shims.Should().ContainSingle().Subject;
            var prompt = shim.Request.StdIn;
            copies.Should().NotBeEmpty("the launch was given a folder of copies");
            foreach (var copy in copies)
            {
                prompt.Should().Contain(copy, "the prompt names the copy Kimi can read");
            }

            prompt.Should().NotContain(Repo, "never the root, which it cannot read");
        }

        if (ending == "refused")
        {
            played.Shims.Should().BeEmpty("the refused row started nothing");
        }

        if (played.Shims.TryPeek(out var first))
        {
            Directory.Exists(RowFolderOf(first.Request)).Should().BeFalse("the row's scratch folder is gone once the row ended");
        }

        TheHomesFolderIsEmpty();
        recorded.Should().BeEmpty("the deleter never met a link: KimiHome unlinked the home's junction first (E5.3 g)");
        if (ending == "refused")
        {
            homeRecorded.Should().BeEmpty("no home was made for a row refused at its copy");
        }
        else
        {
            homeRecorded.Should().ContainSingle().Which.Should().EndWith(Path.Combine("home", "credentials"), "the home's one junction is unlinked before its folder is deleted (E5.3 g)");
        }
        person.Hash().Should().Equal(before, "the fake person's sign-in folder is whole — names, attributes and bytes");
    }
}
