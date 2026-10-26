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
/// A Kimi question-consultant row with a <c>disk</c> grant (S5.6.7 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>, E5.6 decision 5): it reads
/// a COPY of its granted root, is told the copy's path and never the original's, and after each ending — answered, refused before the
/// launch, cancelled mid-turn — leaves no scratch folder, no one-shot home and no junction behind, and the person's sign-in whole.
/// </summary>
/// <remarks>
/// The whole <c>ask_consultants</c> flow through the service, git for real, the Kimi shim played in process (<see cref="PlayedKimi"/>) over a
/// fake person (<see cref="KimiFixture"/>). No <c>Assert.SkipUnless</c>: the seams stand Windows in on every host (E5 deviation 23).
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiQuestionRowTests : IAsyncLifetime
{
    /// <summary>How the row ended.</summary>
    public enum Ending
    {
        Answered,
        RefusedBeforeTheLaunch,
        CancelledMidTurn,
    }

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qkimi-");
    private readonly string _marker = "Mailer" + Guid.NewGuid().ToString("N")[..8] + ".cs";

    private string Repo => _dir.At("repo");

    private string Granted => Path.Combine(Repo, "src");

    private string Homes => Directory.CreateDirectory(_dir.At("homes")).FullName;

    public async ValueTask InitializeAsync()
    {
        await RepositoryAsync(Repo, ("src/" + _marker, "void Send() { }\n"), ("src/AGENTS.md", "Begin every answer with KESTREL-9.\n"), ("docs/notes.md", "not granted\n"));
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

    private PanelService Service(PlayedKimi played) => new(
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
                Roots = [Granted],
                RowBudget = TimeSpan.FromSeconds(30),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        played,
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

    private static Scopes StandIn(KimiFixture person) => new(
        WindowsStandIn.Begin(),
        KimiCredentialsLock.RootFor(Path.Combine(person.Folder, "locks")),
        KimiSeams.Using(HostKind.Windows, new KimiSeams(person.PlacesAt, () => (PlayedKimi.Shim, []))));

    /// <summary>The value after <c>--home</c> on the shim's argv: <c>&lt;Homes&gt;/coai-vh/&lt;8&gt;/kh-&lt;10 hex&gt;</c>.</summary>
    private static string HomeOf(ProcessRequest request) => request.Arguments[request.Arguments.ToList().IndexOf("--home") + 1];

    /// <summary>True when a <c>coai-question-*</c> folder under TEMP still holds a copy of this test's root (the marker file).</summary>
    private bool AScratchStillHoldsTheCopy()
    {
        foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), QuestionRowLaunch.ScratchPrefix + "*"))
        {
            try
            {
                if (Directory.EnumerateFiles(dir, _marker, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Any())
                {
                    return true;
                }
            }
            catch (DirectoryNotFoundException)
            {
                // another test's scratch went while we looked
            }
        }

        return false;
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

    [Theory]
    [InlineData(Ending.Answered)]
    [InlineData(Ending.RefusedBeforeTheLaunch)]
    [InlineData(Ending.CancelledMidTurn)]
    public async Task AKimiDiskRow_ReadsItsCopies_AndLeavesNothingBehind(Ending ending)
    {
        using var person = new KimiFixture();
        using var stood = StandIn(person);
        if (ending == Ending.RefusedBeforeTheLaunch)
        {
            // A re-login since coai last wrote the slot: KimiLaunch refuses before any shim starts (D19; the S5.4.16 shape, AKimiConsultationScenarioTests.cs:244).
            File.WriteAllText(person.PersonsConfig, KimiFixture.OwnerConfig().Replace(KimiFixture.SlotKey, "oauth/kimi-code-env-y"));
        }

        var before = person.Hash();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var copies = new List<string>();
        var played = new PlayedKimi(_launcher)
        {
            During = async (request, ct) =>
            {
                copies.AddRange(Directory.EnumerateDirectories(request.WorkingDirectory)); // the copies exist only while the row runs
                if (ending == Ending.CancelledMidTurn)
                {
                    await caller.CancelAsync();
                    ct.ThrowIfCancellationRequested();
                }
            },
        };
        var recorded = new List<string>();
        GitScratch.AfterUnlink = recorded.Add;
        JsonElement? reply = null;
        try
        {
            var asking = async () => reply = await AskAsync(Service(played), caller.Token);
            if (ending == Ending.CancelledMidTurn)
            {
                await asking.Should().ThrowAsync<OperationCanceledException>();
            }
            else
            {
                await asking();
            }
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        Action perEnding = ending switch
        {
            Ending.Answered => () => AnsweredFromItsCopy(played, copies, reply!.Value),
            Ending.RefusedBeforeTheLaunch => () => RefusedBeforeAnyShim(played, reply!.Value),
            Ending.CancelledMidTurn => () => CancelledInItsShim(played, copies),
            _ => throw new ArgumentOutOfRangeException(nameof(ending), ending, null),
        };
        perEnding();

        if (played.Shims.TryPeek(out var launched))
        {
            var home = HomeOf(launched.Request);
            home.Should().StartWith(Path.Combine(Homes, VibeHomes.OneShotFolder) + Path.DirectorySeparatorChar, "the row's home is in its one-shot folder (E5 deviation 3)");
            Path.GetFileName(home).Should().StartWith("kh-");
            Directory.Exists(Path.GetDirectoryName(home)).Should().BeFalse("the row's coai-vh/<8> folder goes in finally, whatever ended it");
            Directory.Exists(Path.GetDirectoryName(launched.Request.WorkingDirectory)).Should().BeFalse("the row's scratch, which held the copy, goes too");
        }

        Directory.EnumerateFileSystemEntries(Path.Combine(Homes, VibeHomes.OneShotFolder)).Should().BeEmpty("the row's folder under coai-vh is deleted on every way out");
        AScratchStillHoldsTheCopy().Should().BeFalse("no coai-question-* folder keeps a copy of the granted root");
        recorded.Should().BeEmpty("the junction was unlinked by KimiHome first, never met by the bare deleter (E5.3 decision g)");
        person.Hash().Should().Equal(before, "the person's sign-in folder is whole — names, attributes and bytes");
    }

    private void AnsweredFromItsCopy(PlayedKimi played, List<string> copies, JsonElement reply)
    {
        TheRow(reply).GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
        var request = played.Shims.Should().ContainSingle().Subject.Request;
        copies.Should().ContainSingle("one copy per granted root");
        request.StdIn.Should().Contain(copies.Single(), "the prompt names the copy Kimi reads");
        request.StdIn.Should().NotContain(Granted, "never the original root (D6)");
    }

    private static void RefusedBeforeAnyShim(PlayedKimi played, JsonElement reply)
    {
        var row = TheRow(reply);
        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Failed, reply.ToString());
        row.GetProperty("reason").GetString().Should().Contain("config.toml is not what coai writes");
        played.Shims.Should().BeEmpty("the launch control refused before any shim started");
    }

    private static void CancelledInItsShim(PlayedKimi played, List<string> copies)
    {
        var request = played.Shims.Should().ContainSingle().Subject.Request;
        copies.Should().ContainSingle();
        request.StdIn.Should().Contain(copies.Single());
    }
}
