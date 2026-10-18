using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a Vibe disk row tells the person about the repositories it skipped (E1.3 of
/// <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>, D4's last bullet): the place carries its skip notes, and the
/// row writes them into its <c>note</c> field — redacted, since a note names folders on the person's disk and is written down —
/// on the answered arm and on the refused arm alike. The whole flow through the service, git for real, the vibe CLI played.
/// </summary>
public sealed class AVibeDiskRowSaysWhatItSkippedTests : IDisposable
{
    private const string Advice = "Retry with a jittered backoff, three attempts.";

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-vibe-notes-");
    private readonly VibeQuestionRowFixture _rows;

    public AVibeDiskRowSaysWhatItSkippedTests() => _rows = new VibeQuestionRowFixture(_dir, _launcher);

    private string Asking => _dir.At("asking");

    public void Dispose() => _dir.Dispose();

    private async Task<System.Text.Json.JsonElement> AskAsync(PlayedVibe vibe, params string[] roots)
    {
        await _rows.RepositoryAsync(Asking, ("readme.txt", "where the question is asked from"));

        return await VibeQuestionRowFixture.AskAsync(_rows.ServiceFor(vibe, string.Empty, "glm-5-3", roots), Asking, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(VibeFlowHost.Windows)]
    [InlineData(VibeFlowHost.Wsl2)]
    public async Task AParentWithOneGoodAndOneBrokenRepository_Answers_AndTheRowsNoteNamesTheSkippedOne(string mode)
    {
        using var flow = VibeFlowHost.Enter(mode, _dir.At("flow"));
        using var noBudget = VibePathBudget.Using(VibePathBudget.None);
        var parent = Directory.CreateDirectory(_dir.At("parent")).FullName;
        await _rows.RepositoryAsync(Path.Combine(parent, "good"), ("Mailer.cs", "void Send() { }\n"));
        VibeQuestionRowFixture.BrokenCheckout(Path.Combine(parent, "broken"));
        var vibe = new PlayedVibe(_launcher, Advice);

        var reply = await AskAsync(vibe, parent);

        var row = VibeQuestionRowFixture.TheRow(reply);
        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, reply.ToString());
        var note = row.GetProperty("note").GetString();
        note.Should().Contain("was skipped").And.Contain("broken").And.Contain("git does not confirm it as a work tree");
        vibe.Turns.Should().ContainSingle().Subject.Files.Keys.Should().ContainSingle("only the good repository is copied")
            .Which.Should().EndWith("/Mailer.cs");
    }

    [Theory]
    [InlineData(VibeFlowHost.Windows)]
    [InlineData(VibeFlowHost.Wsl2)]
    public async Task AParentWithOnlyABrokenRepository_IsRefused_AndTheRowsNoteStillNamesIt(string mode)
    {
        using var flow = VibeFlowHost.Enter(mode, _dir.At("flow"));
        var parent = Directory.CreateDirectory(_dir.At("parent")).FullName;
        VibeQuestionRowFixture.BrokenCheckout(Path.Combine(parent, "broken"));
        var vibe = new PlayedVibe(_launcher, Advice);

        var reply = await AskAsync(vibe, parent);

        var row = VibeQuestionRowFixture.TheRow(reply);
        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Refused, reply.ToString());
        row.GetProperty("reason").GetString().Should().Contain("holds no Git repository");
        row.GetProperty("note").GetString().Should().Contain("was skipped").And.Contain("broken");
        vibe.Turns.Should().BeEmpty("a refused row launches nothing");
    }

    [Theory]
    [InlineData(VibeFlowHost.Windows)]
    [InlineData(VibeFlowHost.Wsl2)]
    public async Task TheNoteIsRedacted(string mode)
    {
        using var flow = VibeFlowHost.Enter(mode, _dir.At("flow"));
        // Built at run time, so no token-shaped literal sits in the source: a vendor key prefix and thirty-six repeated characters.
        var token = string.Concat("gh", "p_", new string('a', 36));
        var parent = Directory.CreateDirectory(_dir.At("parent")).FullName;
        VibeQuestionRowFixture.BrokenCheckout(Path.Combine(parent, token));
        var vibe = new PlayedVibe(_launcher, Advice);

        var reply = await AskAsync(vibe, parent);

        var note = VibeQuestionRowFixture.TheRow(reply).GetProperty("note").GetString();
        note.Should().Contain("was skipped", "the note says what was skipped");
        note.Should().NotContain(new string('a', 36), "a note names folders on the person's disk and is written down: the token-shaped name is redacted");
        note.Should().Contain("[redacted]");
    }
}
