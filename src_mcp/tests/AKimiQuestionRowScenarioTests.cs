using System.Text.Json;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi question-consultant row through the whole <c>ask_consultants</c> flow (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7):
/// a <c>disk</c> row reads a COPY of its granted root, is told the copy's path and never the root, and leaves nothing behind — its one-shot
/// home under <c>coai-vh\&lt;8&gt;</c> and its scratch — whether the row answered, was refused at its copy, or was cancelled mid-turn.
/// </summary>
/// <remarks>
/// Only the shim is played (<see cref="PlayedKimi"/>); the fake person (<see cref="KimiFixture"/>), the measured host and the lock root are
/// stood in per async flow (<see cref="WindowsStandIn"/>, <see cref="KimiSeams"/>). Git runs for real.
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowScenarioTests : IDisposable
{
    /// <summary>How a row's run ends in this scenario.</summary>
    public enum Ending
    {
        Answered,
        Refused,
        Cancelled,
    }

    private readonly KimiFixture _person = new();

    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");

    private readonly List<string> _unlinked = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    private string OneShotFolder => Path.Combine(Homes, VibeHomes.OneShotFolder);

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        _person.Dispose();
        _dir.Dispose();
    }

    /// <summary>The fake person, the measured host and the lock root the row's Kimi adapter sees.</summary>
    private static IDisposable StandIn(KimiFixture person) => new Scopes(
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

        var launcher = new ProcessLauncher();
        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
    }

    private PanelService Service(PlayedKimi played, string granted) => new(
        new PanelSettings
        {
            ConsultantHomesRoot = Homes,
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("kimi-disk", "moonshot", "kimi", KimiModels.Default, string.Empty, PlayedKimi.Shim, string.Empty, "question-disk", Enabled: true)],
                Roots = [granted],
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

    /// <summary>The row's own folder — the parent of the home its launch was given.</summary>
    private static string RowFolderOf(ProcessRequest request) =>
        Path.GetDirectoryName(ArgumentOf(request, "--home"))!;

    private static string ArgumentOf(ProcessRequest request, string flag) => request.Arguments[request.Arguments.ToList().IndexOf(flag) + 1];

    private int OneShotEntries => Directory.Exists(OneShotFolder) ? Directory.EnumerateFileSystemEntries(OneShotFolder).Count() : 0;

    /// <summary>S5.6.7: the disk row answers from its copy, is refused at its copy, or is cancelled — and in each case leaves no home, no scratch and no recorded unlink, and the person's folder intact.</summary>
    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.Refused)]
    [InlineData(Ending.Cancelled)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        using var stand = StandIn(_person);
        GitScratch.AfterUnlink = path => _unlinked.Add(path);
        // The asking checkout is a repository in every case; only the granted root differs — a folder outside any work tree when refused.
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("docs/notes.md", "not granted\n"));
        var granted = Path.Combine(Repo, "src");
        if (ending == Ending.Refused)
        {
            granted = Directory.CreateDirectory(_dir.At("plain")).FullName;
            await File.WriteAllTextAsync(Path.Combine(granted, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
        }

        var before = _person.Hash();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(new ProcessLauncher())
        {
            Turn = (_, request) => PlayedKimi.Answered(PlayedKimi.SessionOf(request), 19973, 31),
            During = async (_, ct) =>
            {
                if (ending == Ending.Cancelled)
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };

        var asking = async () => await AskAsync(Service(played, granted), caller.Token);

        if (ending == Ending.Cancelled)
        {
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var reply = await asking();
            var row = TheRow(reply);
            row.GetProperty("status").GetString().Should().Be(
                ending == Ending.Answered ? RowOutcomes.Answered : RowOutcomes.Refused, reply.ToString());
            if (ending == Ending.Refused)
            {
                row.GetProperty("reason").GetString().Should().Contain("not inside a Git work tree");
            }
        }

        if (ending == Ending.Refused)
        {
            played.Shims.Should().BeEmpty("nothing of Kimi's starts for a row whose copy was refused");
        }
        else
        {
            var launch = played.Shims.Should().ContainSingle().Subject.Request;
            if (ending == Ending.Answered)
            {
                launch.StdIn.Should().NotContain(granted, "never the original, which the row must not read");
                launch.StdIn.Should().Contain(Path.Combine(launch.WorkingDirectory, "snapshot-"), "the prompt names the copy Kimi can read, never the root");
            }

            Directory.Exists(RowFolderOf(launch)).Should().BeFalse("the row's home and scratch are gone however it ended");
        }

        OneShotEntries.Should().Be(0, "coai-vh\\<8> is empty once the row ends, on every way out");
        _unlinked.Should().BeEmpty("the one-shot home is disposed junction-first");
        _person.Hash().Should().Equal(before, "the fake person's folder is intact");
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
}
