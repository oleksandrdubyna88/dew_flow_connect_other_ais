using Xunit;
using FluentAssertions;
using CoaiMcp.Server;

namespace CoaiMcp.Tests;

/// <summary>
/// The memory that stops the split order being given twice to the same AI.
/// </summary>
/// <remarks>
/// Our own session cannot answer this question: its plan stage happens once, and the epics a split
/// produces come back on their own branches as their own sessions. The CALLER is what carries
/// across them, and Claude Code hands us its session id for free.
/// </remarks>
public sealed class CallerSessionsTests
{
    private static CallerSessions InATempDir(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "coai-callers-" + Guid.NewGuid().ToString("N")[..8]);
        return new CallerSessions(dir);
    }

    [Fact]
    public void ACallerNobodyHasSeen_GetsTheOrder()
    {
        var callers = InATempDir(out _);

        callers.SplitAlreadyOrdered("session-a", DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void TheSameCallerComingBack_DoesNot()
    {
        var callers = InATempDir(out _);
        var now = DateTime.UtcNow;

        callers.RecordSplitOrder("session-a", now);

        callers.SplitAlreadyOrdered("session-a", now.AddMinutes(20)).Should().BeTrue(
            "this is the epic asking for its own plan review, and it is already a piece of a split");
    }

    [Fact]
    public void ADifferentCaller_IsNotTheSameTask()
    {
        var callers = InATempDir(out _);
        callers.RecordSplitOrder("session-a", DateTime.UtcNow);

        callers.SplitAlreadyOrdered("session-b", DateTime.UtcNow).Should().BeFalse(
            "a second Claude working on something else is owed its own split order");
    }

    [Fact]
    public void ItSurvivesTheServer()
    {
        // The server restarts between rounds — an MCP client reconnecting respawns it — so a memory
        // held in a field would forget exactly when the epics start coming back.
        var dir = Path.Combine(Path.GetTempPath(), "coai-callers-" + Guid.NewGuid().ToString("N")[..8]);
        new CallerSessions(dir).RecordSplitOrder("session-a", DateTime.UtcNow);

        new CallerSessions(dir).SplitAlreadyOrdered("session-a", DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void AfterADay_TheCallerIsANewTaskAgain()
    {
        var callers = InATempDir(out _);
        var yesterday = DateTime.UtcNow - CallerSessions.Remembers - TimeSpan.FromMinutes(1);

        callers.RecordSplitOrder("session-a", yesterday);

        callers.SplitAlreadyOrdered("session-a", DateTime.UtcNow).Should().BeFalse(
            "a session long enough to span a day is a session doing more than one thing");
    }

    [Fact]
    public void AnIdWithSlashesAndColons_IsStillOneCaller()
    {
        // A session id is somebody else's string. It reached a file name unescaped once in this
        // repository already (the engine-lease slug), and the failure mode is a caller silently
        // sharing a memory with another.
        var callers = InATempDir(out _);
        var awkward = "claude/2026-09-04T10:11:12Z?x=1";

        callers.RecordSplitOrder(awkward, DateTime.UtcNow);

        callers.SplitAlreadyOrdered(awkward, DateTime.UtcNow).Should().BeTrue();
        callers.SplitAlreadyOrdered("claude/2026-09-04T10:11:12Z?x=2", DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void AStampFromTheFuture_IsNotTrusted()
    {
        // A clock that moved backwards would otherwise remember a caller for a day it never had.
        CallerSessions.StillRemembered(DateTime.UtcNow.AddHours(2), DateTime.UtcNow).Should().BeFalse();
    }

    // ---------- who is calling ----------

    [Fact]
    public void ClaudeCodesOwnSessionId_IsWhatWeUse()
    {
        // Measured in this session: Claude Code exports CLAUDE_CODE_SESSION_ID to every child it
        // spawns, and an MCP server on stdio is one of those children.
        CallerIdentity.From(name => name == "CLAUDE_CODE_SESSION_ID" ? "e82c065c" : null)
            .Should().Be("e82c065c");
    }

    [Fact]
    public void AnExplicitOverride_Wins()
    {
        // For a client that has no session id of its own: the operator can give it one.
        CallerIdentity.From(name => name switch
        {
            "COAI_CALLER_SESSION" => "mine",
            "CLAUDE_CODE_SESSION_ID" => "theirs",
            _ => null,
        }).Should().Be("mine");
    }

    [Fact]
    public void ABlankVariable_IsNotAnIdentity()
    {
        // An exported-but-empty variable is how a client that sets nothing looks on some shells.
        CallerIdentity.From(name => name == "CLAUDE_CODE_SESSION_ID" ? "   " : null)
            .Should().BeEmpty();
    }

    [Fact]
    public void AClientThatSaysNothing_IsEmpty_AndTheServerFallsBackToItsOwnSession() =>
        CallerIdentity.From(_ => null).Should().BeEmpty();
}
