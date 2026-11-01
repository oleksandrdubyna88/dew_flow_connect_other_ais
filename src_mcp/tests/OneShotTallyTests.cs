using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a one-shot place says while it works (E1 of <c>research/PLAN_vibe_reads_a_folder_of_repositories.md</c>, code gate finding 5 and 13):
/// every note goes through ONE road, <see cref="OneShotTally.Say"/>, and a note names folders on the person's disk — so it is redacted
/// there, once, for the log's sink and for the place's own list alike.
/// </summary>
public sealed class OneShotTallyTests
{
    /// <summary>A token-shaped folder name, built at run time so that no token-shaped literal sits in the source.</summary>
    private static string TokenShaped() => string.Concat("gh", "p_", new string('A', 36));

    [Fact]
    public void ANoteNamingATokenShapedFolder_IsRedactedForTheSinkAndForTheNotes_AlikeAndOnce()
    {
        var token = TokenShaped();
        List<string> said = [];
        var tally = new OneShotTally(said.Add);

        tally.Say($"D:/work/{token}/repo was skipped: it could not be listed (Access is denied)");

        said.Should().ContainSingle().Which.Should().NotContain(token).And.Contain(Redaction.Redacted).And.Contain("was skipped");
        tally.Notes.Should().Equal(said, "the log and the row's note read the same redacted text");
    }

    [Fact]
    public void ALimitation_IsSaidRedacted_AndKeptForThePromptAsItIs()
    {
        List<string> said = [];
        var tally = new OneShotTally(said.Add);

        tally.Limit("skipped 2 linked worktrees whose main checkouts are copied");

        tally.Limitations.Should().Equal("skipped 2 linked worktrees whose main checkouts are copied");
        tally.Notes.Should().Equal(said).And.ContainSingle();
    }
}
