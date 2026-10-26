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
        Directory.CreateDirectory(Path.Combine(Repo, "src"));
        Directory.CreateDirectory(Path.Combine(Repo, "docs"));
        await File.WriteAllTextAsync(Path.Combine(Repo, "src", "Mailer.cs"), "void Send() { }\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Repo, "src", "AGENTS.md"), "Begin every answer with KESTREL-9.\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(Repo, "docs", "notes.md"), "not granted\n", TestContext.Current.CancellationToken);

        foreach (var args in (string[][])[["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]])
        {
            var result = await _launcher.RunAsync(new ProcessRequest("git", args, Repo), TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
        }
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

    /// <summary>One <c>ask_consultants</c> call over <see cref="Repo"/>; the reply's JSON.</summary>
    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken ct)
    {
        return JsonDocument.Parse(await service.AskConsultantsAsync(Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct)).RootElement;
    }

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    /// <summary>The shim's argument after <paramref name="flag"/> (<c>--cwd</c>, <c>--home</c>).</summary>
    private static string ArgumentAfter(ProcessRequest request, string flag)
    {
        var arguments = request.Arguments.ToList();

        return arguments[arguments.IndexOf(flag) + 1];
    }

    /// <summary>S5.6.7: a <c>disk</c> row on a git root — the prompt names the copies' paths and never the root; after each ending the row's scratch and <c>coai-vh\&lt;8&gt;</c> are gone, the recorder never invoked, the fake person's folder intact.</summary>
    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.RefusedBeforeTheLaunch)]
    [InlineData(Ending.CancelledMidTurn)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        using var stood = KimiStandIn.Of(_person);
        var before = _person.Hash();
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(_launcher)
        {
            During = async (_, ct) =>
            {
                if (ending == Ending.CancelledMidTurn)
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };
        var granted = Path.Combine(Repo, "src");
        if (ending == Ending.RefusedBeforeTheLaunch)
        {
            granted = Directory.CreateDirectory(_dir.At("plain")).FullName;
            await File.WriteAllTextAsync(Path.Combine(granted, "notes.txt"), "not a repository", TestContext.Current.CancellationToken);
        }

        if (ending == Ending.CancelledMidTurn)
        {
            var asking = async () => await AskAsync(Service(played, granted), caller.Token);

            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var reply = await AskAsync(Service(played, granted), caller.Token);

            if (ending == Ending.Answered)
            {
                TheRow(reply).GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
                var seen = played.Shims.Should().ContainSingle().Subject.Request;
                var copy = ArgumentAfter(seen, "--cwd");
                seen.StdIn.Should().Contain(copy, "the prompt names the copy the row reads");
                seen.StdIn.Should().NotContain(granted, "never the root itself");
            }
            else
            {
                var row = TheRow(reply);
                row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused, reply.ToString());
                row.GetProperty("reason").GetString().Should().Contain("not inside a Git work tree");
                played.Shims.Should().BeEmpty("nothing of Kimi's starts for a row whose copy was refused");
            }
        }

        foreach (var launch in played.Shims)
        {
            Directory.Exists(Path.GetDirectoryName(ArgumentAfter(launch.Request, "--cwd"))).Should().BeFalse("the row's scratch, with its copies, is gone");
            Directory.Exists(Path.GetDirectoryName(ArgumentAfter(launch.Request, "--home"))).Should().BeFalse("the row's one-shot folder is gone");
        }

        Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }
}
