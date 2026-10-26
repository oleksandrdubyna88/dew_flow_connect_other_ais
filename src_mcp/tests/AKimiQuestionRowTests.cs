using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Consultation;
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
        // QWEN-FILL-BEGIN s567-repo test
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
        // QWEN-FILL-END s567-repo
    }

    private static IDisposable StandIn(KimiFixture person)
    {
        // QWEN-FILL-BEGIN s567-standin test
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
        // QWEN-FILL-END s567-standin
    }

    private PanelService Service(PlayedKimi played, string homesRoot, params string[] roots)
    {
        // QWEN-FILL-BEGIN s567-service test
        var settings = new PanelSettings
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
        };
        return new PanelService(settings, VaultKeys.None("no vault in tests"), default, played, Logger.None, Noticing.None);
        // QWEN-FILL-END s567-service
    }

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct)
    {
        // QWEN-FILL-BEGIN s567-ask test
        return JsonDocument.Parse(await service.AskConsultantsAsync(Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;
        // QWEN-FILL-END s567-ask
    }

    // ---------- S5.6.7 ----------

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        // QWEN-FILL-BEGIN s567 test
        using var stood = StandIn(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        Directory.CreateDirectory(Homes);

        var granted = Path.Combine(Repo, "src");
        if (ending == "refused")
        {
            var plain = _dir.At("plain");
            Directory.CreateDirectory(plain);
            await File.WriteAllTextAsync(Path.Combine(plain, "file.txt"), "x", TestContext.Current.CancellationToken);
            granted = plain;
        }

        var started = new TaskCompletionSource();
        var played = new PlayedKimi(_launcher)
        {
            Turn = (n, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31, Advice),
            During = async (request, ct) =>
            {
                if (ending == "cancelled")
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct);
                }
            }
        };

        CancellationTokenSource cts = null!;
        if (ending == "cancelled")
        {
            cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                await started.Task;
                cts.Cancel();
            });
        }

        var reply = await AskAsync(Service(played, Homes, granted), cts?.Token ?? TestContext.Current.CancellationToken);

        var row = reply.GetProperty("answers").EnumerateArray().Single();
        string status = row.GetProperty("status").GetString()!;

        if (ending == "answered")
        {
            status.Should().Be(RowOutcomes.Answered, reply.ToString());
            played.Shims.Should().ContainSingle();
            var launch = played.Shims.Dequeue();
            launch.Request.StdIn.Should().Contain(Path.Combine(launch.Request.WorkingDirectory, "Mailer.cs"), "the prompt names the copy Vibe can read");
            launch.Request.StdIn.Should().NotContain(granted, "never the original, which it cannot read (M-V11)");
        }
        else if (ending == "refused")
        {
            status.Should().Be(RowOutcomes.Refused, reply.ToString());
            played.Shims.Should().BeEmpty();
        }
        else
        {
            status.Should().BeOneOf(RowOutcomes.TimedOut, RowOutcomes.Blocked, RowOutcomes.Failed, "the cancelled row is not answered");
            played.Shims.Should().BeEmpty();
        }

        Directory.Exists(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeFalse("the one-shot home is gone");
        if (Directory.Exists(Path.Combine(Homes, VibeHomes.OneShotFolder)))
        {
            Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the one-shot home holds no entry");
        }

        if (played.Shims.Any())
        {
            var last = played.Shims.LastOrDefault();
            Directory.Exists(last.Request.WorkingDirectory).Parent!.Exists.Should().BeFalse("the row's scratch is gone");
        }

        _unlinkedByTheDeleter.Should().BeEmpty("no junctions were recorded by the deleter");
        _person.Hash().Should().Equal(before, "the person's folder is untouched");
        // QWEN-FILL-END s567
    }
}
