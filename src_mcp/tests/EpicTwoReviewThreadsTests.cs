using System.Diagnostics;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// PR #686's review threads (CodeRabbit): a consultation's prompt file goes when its launch does, the
/// <c>--check-model</c> dispatch is the real binary's, a words-only check does not judge runs it cannot resolve, and an
/// "always" card is told its words are not used.
/// </summary>
public sealed class EpicTwoReviewThreadsTests
{
    /// <summary>Answers every launch with a clean exit, or throws as a cancelled launch does.</summary>
    private sealed class Answering(bool throws = false) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            throws
                ? throw new OperationCanceledException()
                : Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, false));
    }

    private static (ReviewerInvocation Invocation, string File) WithAPromptFile()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory("coai-consult-file-").FullName, "turn.prompt");
        File.WriteAllText(file, "the consultation's prompt");
        var invocation = new AntigravityConsultant(new AntigravityRuntime()).Build(new ConsultantLaunch(
            Path.GetTempPath(), "where is the sweep invoked?", string.Empty, Path.GetTempPath(),
            new ReviewerSettings("antigravity") { Timeout = TimeSpan.FromMinutes(1) }));

        return (invocation with { TempFiles = [file] }, file);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AConsultationTurn_LeavesNoPromptFile_HoweverItEnds(bool cancelled)
    {
        var (first, file) = WithAPromptFile();

        var run = () => ConsultantTurn.RunAsync(
            new ReviewerExecutor(new Answering(cancelled)),
            new AntigravityConsultant(new AntigravityRuntime()),
            first,
            _ => Task.FromResult<IReadOnlyList<TreeChange>>([]),
            _ => { },
            TestContext.Current.CancellationToken);

        if (cancelled)
        {
            await run.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await run();
        }

        File.Exists(file).Should().BeFalse("the scheduler's cleanup never runs for a consultation, so its own launch must");
    }

    [Fact]
    public async Task CheckModel_IsDispatchedToTheModelCheck_NotTheCallerKindsCheck()
    {
        var dir = Directory.CreateTempSubdirectory("coai-check-model-").FullName;
        var info = new ProcessStartInfo(ServerBinary.Path)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("--check-model");
        // Its own store: a test child must never read or write the machine's.
        info.Environment["COAI_DATA_DIR"] = dir;

        using var process = Process.Start(info)!;
        await process.StandardInput.WriteAsync("{}");
        process.StandardInput.Close();
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        process.ExitCode.Should().Be(65);
        (await stderr).Should().Contain("--check-model reads", "the model check answered, not --check-consultant asking for --caller");
    }

    [Fact]
    public void AWordsOnlyCheck_DoesNotJudgeTheRunsItCannotResolve()
    {
        var (code, output, _) = CheckSecurityMode.Answer("""
            {"text":"x","lane":{"enabled":true,"runs":[{"vendor":"codex","prompt":"redteam-sql"}]}}
            """, validate: true);

        code.Should().Be(0);
        output.Should().NotContain("pair", "a run names a reviewer this check has no list of — that is the panel's question");
    }

    [Fact]
    public void AnAlwaysCardsWords_AreSaidToBeUnused()
    {
        var lane = SecurityLaneSetting.Parse("""
            {"enabled":true,"prompts":[{"id":"redteam-general","words":["billing"]}],"runs":[]}
            """, []);

        lane.Complaints.Should().Contain(c => c.Contains("redteam-general") && c.Contains("words"),
            "an always card runs on every change, and a word on it would otherwise do nothing in silence");
    }

    [Fact]
    public void AnAlwaysCardWithNoWords_IsNotComplainedAbout() =>
        SecurityLaneSetting.Parse("""{"enabled":true,"prompts":[{"id":"redteam-general","words":[]}],"runs":[]}""", [])
            .Complaints.Should().BeEmpty();
}
