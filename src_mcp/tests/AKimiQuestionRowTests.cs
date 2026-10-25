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
/// A Kimi <c>disk</c> question-consultant row (E5.6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, S5.6.7): it reads COPIES of its granted roots, the
/// prompt names the copies and never the roots, and the row's scratch and its one-shot home under <c>coai-vh</c> are gone junction-first once the row
/// ends — answered, refused before the launch, or cancelled in flight.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the real launch control over a fake person's sign-in
/// (<see cref="KimiFixture"/>); only the shim is played (<see cref="PlayedKimi"/>). The fake person lives outside the homes root the row deletes.
/// </remarks>
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(Repo, "src"));
        await File.WriteAllTextAsync(Path.Combine(Repo, "src", "Mailer.cs"), "void Send() { }\n", TestContext.Current.CancellationToken);
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

    private async Task<JsonElement> AskAsync(PanelService service, CancellationToken? ct = null)
    {
        using var standIn = WindowsStandIn.Begin();
        using var locks = KimiCredentialsLock.RootFor(Path.Combine(_person.Folder, "locks"));
        using var seams = KimiSeams.Using(HostKind.Windows, new KimiSeams(_person.PlacesAt, () => (PlayedKimi.Shim, [])));

        return JsonDocument.Parse(await service.AskConsultantsAsync(
            Repo, "Which helper retries an outbound send?", "I tried a fixed wait.", ct: ct ?? TestContext.Current.CancellationToken)).RootElement;
    }

    private static JsonElement TheRow(JsonElement reply)
    {
        reply.TryGetProperty("answers", out var answers).Should().BeTrue(reply.ToString());

        return answers.EnumerateArray().Should().ContainSingle().Subject;
    }

    private void NothingIsLeftBehind(IReadOnlyList<string> before)
    {
        Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");
    }

    /// <summary>Answered; refused before the launch; cancelled mid-turn — the row leaves nothing, and the person's sign-in stays whole.</summary>
    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
        var before = _person.Hash();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var played = new PlayedKimi(_launcher)
        {
            During = async (_, ct) =>
            {
                if (ending == "cancelled")
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };
        var granted = ending == "refused" ? Directory.CreateDirectory(_dir.At("plain")).FullName : Path.Combine(Repo, "src");

        var asking = async () => await AskAsync(Service(played, granted), caller.Token);

        if (ending == "cancelled")
        {
            await asking.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var reply = await asking();
            TheRow(reply).GetProperty("status").GetString().Should().Be(ending == "answered" ? RowOutcomes.Answered : RowOutcomes.Refused, reply.ToString());
        }

        NothingIsLeftBehind(before);
        if (ending == "refused")
        {
            played.Shims.Should().BeEmpty("a root that cannot be copied launches nothing");
            return;
        }

        var request = played.Shims.Should().ContainSingle().Subject.Request;
        var cwd = KimiEnvironment.FlagOf(request, "--cwd");
        Directory.Exists(cwd).Should().BeFalse("the copy is gone with the row's scratch");
        request.StdIn.Should().Contain(cwd, "the prompt names the copies' paths")
            .And.NotContain(Path.Combine(Repo, "src"), "never the root itself");
    }
}
