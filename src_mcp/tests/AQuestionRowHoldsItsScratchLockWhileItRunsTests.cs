using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A question row says it is alive by HOLDING a lock file in its scratch folder (D8 of
/// <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>): the row's budget has no upper bound, so the scratch sweep cannot tell a
/// live row from a stranded one by age. The sweep's half is <c>AScratchSweepRunsOnTheConsultationBeatTests</c>; this is the row's.
/// </summary>
/// <remarks>The Vibe CLI is played in process (<see cref="PlayedVibe"/>), git is real, and the lock is probed from inside the turn.</remarks>
public sealed class AQuestionRowHoldsItsScratchLockWhileItRunsTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qrow-lock-");

    private string Repo => _dir.At("repo");

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
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

    private Task<QuestionRowRecord> RunRowAsync(PlayedVibe vibe)
    {
        var admitted = (RowAdmission.Admitted)QuestionAdmission.Admit(
            new QuestionRow("vibe-disk", "mistral", "vibe", "glm-5-3", string.Empty, PlayedVibe.Exe, string.Empty, "question-disk", Enabled: true),
            new QuestionConsultSettings { Roots = [Repo] }, [], Core.Api.ApiOverrides.None);
        var input = new RowLaunchInput(
            admitted, "Which helper retries?", new ReviewerSettings("vibe") { ExecutablePath = PlayedVibe.Exe, Model = "glm-5-3", Timeout = TimeSpan.FromSeconds(30) },
            Repo, Directory.CreateDirectory(_dir.At("answers")).FullName, string.Empty, TimeSpan.FromSeconds(30))
        {
            ChangesSoFar = _ => Task.FromResult<IReadOnlyList<Core.Consultation.TreeChange>>([]),
            PromptNaming = copies => "Read " + string.Join(", ", copies),
        };
        var launch = new QuestionRowLaunch(new ReviewerExecutor(vibe), new UsageLedger(_dir.At("data")), Logger.None, vibe);

        return launch.RunAsync(
            input, new QuestionRowRecord("vibe-disk", "mistral", "glm-5-3", "vibe", "question-disk", "Projects on this disk", "disk", "default-deny"), TestContext.Current.CancellationToken);
    }

    /// <summary>The lock as the row's own turn sees it, and where the row's scratch was — recorded INSIDE the launch.</summary>
    private sealed class LockSeenByTheTurn
    {
        public LockProbe Probe { get; set; } = LockProbe.Unknown;

        public string Scratch { get; set; } = string.Empty;
    }

    [Theory]
    [InlineData(VibeFlowHost.Windows)]
    [InlineData(VibeFlowHost.Wsl2)]
    public async Task ARowThatIsRunning_HoldsItsScratchLock_AndLetsGoOfItBeforeTheScratchIsDeleted(string mode)
    {
        using var flow = VibeFlowHost.Enter(mode, _dir.At("flow"));
        using var noBudget = VibePathBudget.Using(VibePathBudget.None);
        var seen = new LockSeenByTheTurn();
        var vibe = new PlayedVibe(_launcher, "Retry with a jittered backoff.")
        {
            AnswerFor = turn =>
            {
                // The copies live in <scratch>/<tree>/…, so the scratch is the parent of the turn's working folder.
                seen.Scratch = Path.GetDirectoryName(turn.Request.WorkingDirectory)!;
                seen.Probe = HeldFile.Probe(Path.Combine(seen.Scratch, QuestionRowLaunch.LockName));

                return "Retry with a jittered backoff.";
            },
        };

        var record = await RunRowAsync(vibe);

        record.Status.Should().Be(RowOutcomes.Answered, record.Reason);
        seen.Probe.Should().Be(LockProbe.Held, "a row holds .coai-question.lock in its scratch while it runs, so the sweep can tell it is alive");
        Path.GetFileName(seen.Scratch).Should().StartWith(QuestionRowLaunch.ScratchPrefix);
        Directory.Exists(seen.Scratch).Should().BeFalse("the row let go of its lock and then removed its scratch — a held lock file would have kept the folder on Windows");
    }
}
