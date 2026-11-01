using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a Vibe disk row's place skipped reaches the row's <c>note</c> on EVERY way the row can end (E1.3 of
/// <c>research/PLAN_vibe_reads_a_folder_of_repositories.md</c> wrote it for the answered and the refused arm; the review of
/// 2026-10-18 found the timed-out and the failed arm still dropped it, and a follow-up turn replacing the seed).
/// </summary>
/// <remarks>The Vibe CLI is played in process (<see cref="PlayedVibe"/>); git is real.</remarks>
public sealed class AQuestionRowKeepsWhatItsPlaceSkippedWhateverEndsItTests : IDisposable
{
    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-qrow-notes-");

    public void Dispose() => _dir.Dispose();

    /// <summary>A parent holding one good repository and one broken checkout: the place answers Owned, with one note.</summary>
    private async Task<string> ParentWithOneBrokenAsync()
    {
        var parent = Directory.CreateDirectory(_dir.At("parent")).FullName;
        var good = Path.Combine(parent, "good");
        Directory.CreateDirectory(good);
        await File.WriteAllTextAsync(Path.Combine(good, "Mailer.cs"), "void Send() { }\n", TestContext.Current.CancellationToken);
        await GitAsync(good, ["init", "-b", "main"]);
        VibeQuestionRowFixture.BrokenCheckout(Path.Combine(parent, "broken"));

        return parent;
    }

    private async Task GitAsync(string at, string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
    }

    private Task<QuestionRowRecord> RunRowAsync(PlayedVibe vibe, string parent, Func<CopiedRoots, string>? naming)
    {
        var admitted = (RowAdmission.Admitted)QuestionAdmission.Admit(
            new QuestionRow("vibe-disk", "mistral", "vibe", "glm-5-3", string.Empty, PlayedVibe.Exe, string.Empty, "question-disk", Enabled: true),
            new QuestionConsultSettings { Roots = [parent] }, [], Core.Api.ApiOverrides.None);
        var input = new RowLaunchInput(
            admitted, "Which helper retries?", new ReviewerSettings("vibe") { ExecutablePath = PlayedVibe.Exe, Model = "glm-5-3", Timeout = TimeSpan.FromSeconds(30) },
            parent, Directory.CreateDirectory(_dir.At("answers")).FullName, string.Empty, TimeSpan.FromSeconds(30))
        {
            ChangesSoFar = _ => Task.FromResult<IReadOnlyList<Core.Consultation.TreeChange>>([]),
        };
        if (naming is not null)
        {
            input = input with { PromptNaming = naming };
        }

        var launch = new QuestionRowLaunch(new ReviewerExecutor(vibe), new UsageLedger(_dir.At("data")), Logger.None, vibe);

        return launch.RunAsync(
            input, new QuestionRowRecord("vibe-disk", "mistral", "glm-5-3", "vibe", "question-disk", "Projects on this disk", "disk", "default-deny"), TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(VibeFlowHost.Windows)]
    [InlineData(VibeFlowHost.Wsl2)]
    public async Task ARowThatFails_AfterItsPlaceWasMade_StillCarriesWhatThePlaceSkipped(string mode)
    {
        using var flow = VibeFlowHost.Enter(mode, _dir.At("flow"));
        using var noBudget = VibePathBudget.Using(VibePathBudget.None);
        var parent = await ParentWithOneBrokenAsync();

        // No prompt naming the copies: the launch is a contract violation, so the row ends Failed AFTER its place was made.
        var record = await RunRowAsync(new PlayedVibe(_launcher, "never reached"), parent, naming: null);

        record.Status.Should().Be(RowOutcomes.Failed, record.Reason);
        record.Note.Should().Contain("was skipped").And.Contain("broken");
    }

    [Theory]
    [InlineData(VibeFlowHost.Windows)]
    [InlineData(VibeFlowHost.Wsl2)]
    public async Task ARowThatRunsPastItsDeadline_StillCarriesWhatThePlaceSkipped(string mode)
    {
        using var flow = VibeFlowHost.Enter(mode, _dir.At("flow"));
        using var noBudget = VibePathBudget.Using(VibePathBudget.None);
        var parent = await ParentWithOneBrokenAsync();
        // The row's own deadline firing, as the launcher reports it from inside a turn: a cancellation the caller did not ask for.
        var vibe = new PlayedVibe(_launcher, "never reached") { During = _ => throw new OperationCanceledException() };

        var record = await RunRowAsync(vibe, parent, copies => "Read " + string.Join(", ", copies.Paths));

        record.Status.Should().Be(RowOutcomes.TimedOut, record.Reason);
        record.Note.Should().Contain("was skipped").And.Contain("broken");
    }

    [Fact]
    public void ANextTurnsNote_IsAddedToTheSeed_NeverReplacesIt()
    {
        var seed = "C:\\x\\broken was skipped";
        var next = new CoaiMcp.Runners.Consultation.AnsweringTurn.Next(
            new CoaiMcp.Runners.Consultation.ConsultantLaunch("repo", "prompt", string.Empty, "answers", new ReviewerSettings("vibe"), string.Empty),
            CoaiMcp.Runners.Consultation.AnsweringMemory.Start("base"), "turn 2: nothing to serve");

        QuestionRowLaunch.NoteAfter(seed, next).Should().Be(seed + "; turn 2: nothing to serve");
        QuestionRowLaunch.NoteAfter(string.Empty, next).Should().Be("turn 2: nothing to serve", "a row with nothing skipped keeps the turn's own note as before");
    }
}
