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

/// <summary>S5.6.7 of todo/PLAN_vibe_and_kimi_runtimes_E5.md: a Kimi disk row through the service — in a copy of its root, on a one-shot home, gone junction-first on every ending.</summary>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-qrow-");
    private readonly KimiFixture _person = new();
    private readonly List<string> _unlinkedByTheDeleter = [];

    private string Repo => _dir.At("repo");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    private string OneShots => Path.Combine(Homes, VibeHomes.OneShotFolder);

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/Mailer.cs", "void Send() { }\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"));
        GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;
    }

    public ValueTask DisposeAsync()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
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

    /// <summary>The person, the host and the lock root this flow's Kimi adapters see — set by a SYNCHRONOUS call, as in <c>AKimiConsultationScenarioTests</c>.</summary>
    private static IDisposable StandIn(KimiFixture person) => new Scopes(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>The service with a homes root (a home-keeping row reserves its one-shot folder there) and ONE Kimi disk row.</summary>
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

    private static Func<ProcessRequest, CancellationToken, Task> CancelThenWait(CancellationTokenSource cancel) => async (_, ct) =>
    {
        await cancel.CancelAsync();
        await Task.Delay(Timeout.Infinite, ct);
    };

    private static string HomeOf(ProcessRequest request) => request.Arguments[request.Arguments.ToList().IndexOf("--home") + 1];

    [Theory]
    [InlineData("answered")]
    [InlineData("refused")]
    [InlineData("cancelled")]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(string ending)
    {
        using var stood = StandIn(_person);
        var granted = Path.Combine(Repo, "src");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (ending == "refused")
        {
            // Not signed in: the launch control refuses a first launch before anything starts (KimiLaunch.Rendered, "has no credentials folder").
            // A re-login refuses only a follow-up turn, so it cannot stand in for a first launch.
            Directory.Delete(_person.Credentials, recursive: true);
        }

        var before = _person.Hash();
        var played = new PlayedKimi(_launcher) { During = ending == "cancelled" ? CancelThenWait(cancel) : static (_, _) => Task.CompletedTask };

        JsonElement? reply = null;
        try
        {
            reply = await AskAsync(Service(played, granted), cancel.Token);
        }
        catch (OperationCanceledException) when (ending == "cancelled")
        {
        }

        switch (ending)
        {
            case "answered":
                {
                    var row = reply!.Value.GetProperty("answers").EnumerateArray().Should().ContainSingle().Subject;
                    row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
                    var request = played.Shims.Should().ContainSingle().Subject.Request;
                    request.StdIn.Should().Contain(request.WorkingDirectory, "the prompt names the copy Kimi reads");
                    request.StdIn.Should().NotContain(granted, "never the root itself");
                    request.Arguments.Should().NotContain(granted);
                    Directory.Exists(request.WorkingDirectory).Should().BeFalse("the copy is gone once the row answered");
                    Directory.Exists(Path.GetDirectoryName(HomeOf(request))).Should().BeFalse("the row's coai-vh/<8> is gone");
                    Path.GetDirectoryName(Path.GetDirectoryName(HomeOf(request))).Should().Be(OneShots, "the row's home is one-shot under the homes root");
                    break;
                }

            case "refused":
                {
                    played.Shims.Should().BeEmpty("the launch control refused before anything started");
                    var row = reply!.Value.GetProperty("answers").EnumerateArray().Should().ContainSingle().Subject;
                    row.GetProperty("status").GetString().Should().BeOneOf(RowOutcomes.Failed, RowOutcomes.Refused, reply.ToString());
                    break;
                }

            case "cancelled":
                {
                    var shim = played.Shims.Should().ContainSingle().Subject;
                    Directory.Exists(Path.GetDirectoryName(HomeOf(shim.Request))).Should().BeFalse();
                    Directory.Exists(shim.Request.WorkingDirectory).Should().BeFalse();
                    if (reply is { } answered)
                    {
                        answered.GetProperty("answers").EnumerateArray().Single().GetProperty("status").GetString().Should().NotBe(RowOutcomes.Answered);
                    }

                    break;
                }
        }

        (Directory.Exists(OneShots) ? Directory.EnumerateDirectories(OneShots) : []).Should().BeEmpty("no one-shot folder is left behind");
        _unlinkedByTheDeleter.Should().BeEmpty("KimiHome unlinks the junction before the deleter walks the folder (E5.3 g)");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
    }
}
