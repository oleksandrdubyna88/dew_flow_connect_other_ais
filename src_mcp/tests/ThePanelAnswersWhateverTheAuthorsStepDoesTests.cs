using CoaiMcp.Core.Authors;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Through the real engine with FakeCli (E5.S4): whatever the authors step finds, the round answers and is saved — and the suite never reads a
/// developer's own transcripts. The caller's identity is the service's INJECTED environment, never the process's, and every transcripts folder
/// here is a temp one; the session is opened as a Claude Code client because the round's vendor comes from the process's own identity at
/// <c>open</c>, and a test process that holds only a Codex variable would otherwise record <c>no reader for codex</c>.
/// </summary>
[Collection("fakecli-env")]
public sealed class ThePanelAnswersWhateverTheAuthorsStepDoesTests : FakeCliGateBase
{
    /// <summary>An EMPTY transcripts folder inside the test's own temp directory: the suite never reads a developer's.</summary>
    private string EmptyProjects => Directory.CreateDirectory(Temp.At("projects")).FullName;

    private static string RealLookingId => Guid.NewGuid().ToString("D");

    private async Task<(PanelService Service, RoundRecordView Round)> PlanRoundAsync(Func<string, string?> environment, string transcripts)
    {
        var service = Service(environment, transcripts);
        await service.OpenAsync(Repo, "feature", client: "claude-code");
        var answer = Parse(await service.ReviewPlanAsync(Repo, "feature", "a plan nobody objects to"));
        answer.GetProperty("verdict").GetString().Should().Be("proceed", $"the round answered: {answer}");

        var saved = Saved().Rounds.Single(round => round.Stage == "PlanReview");

        return (service, new RoundRecordView(saved.Authors, saved.Status));
    }

    private sealed record RoundRecordView(AuthorsSnapshot? Authors, string Status);

    [Fact]
    public async Task AnEnvironmentWithNoIdentity_RecordsNoSessionId_WhateverTheProcessHolds()
    {
        var (_, round) = await PlanRoundAsync(_ => null, EmptyProjects);

        round.Authors.Should().NotBeNull("a settled plan round carries a snapshot");
        round.Authors!.Render().Should().Be("unobserved: no session id", "the step reads its INJECTED environment: run inside Claude Code the process itself holds a real id");
    }

    [Fact]
    public async Task ARealLookingIdAgainstAnEmptyHome_RecordsTranscriptNotFound()
    {
        var id = RealLookingId;

        var (_, round) = await PlanRoundAsync(name => name == "CLAUDE_CODE_SESSION_ID" ? id : null, EmptyProjects);

        round.Authors!.Render().Should().Be("unobserved: transcript not found", "the temp home holds no file of that id — and nothing reads a developer's ~/.claude");
        round.Authors.Session.Should().Be(id);
        round.Authors.IdSource.Should().Be(CallerSessionId.Environment);
    }

    [Fact]
    public async Task AFailureInsideTheStep_NeverFailsTheRound_ItIsAnsweredAndSavedUnobserved()
    {
        // A folder name no file system accepts: the locator's directory listing throws ArgumentException, which is not an IO error — a
        // failure of the step itself, and the round must still answer and be saved.
        var (_, round) = await PlanRoundAsync(name => name == "CLAUDE_CODE_SESSION_ID" ? RealLookingId : null, EmptyProjects + "\0");

        round.Status.Should().Be("done", "the round is saved whatever the step did");
        round.Authors!.Render().Should().Be("unobserved: reader failed");
        (round.Authors.Reader, round.Authors.Attribution, round.Authors.UncoveredWrites).Should().Be((0, 0, -1));
    }
}
