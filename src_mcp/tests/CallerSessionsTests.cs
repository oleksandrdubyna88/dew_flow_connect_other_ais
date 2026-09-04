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

        callers.TryClaimSplitOrder("session-a", now).Should().BeTrue();

        callers.SplitAlreadyOrdered("session-a", now.AddMinutes(20)).Should().BeTrue(
            "this is the epic asking for its own plan review, and it is already a piece of a split");
        callers.TryClaimSplitOrder("session-a", now.AddMinutes(20)).Should().BeFalse();
    }

    [Fact]
    public void TheClaimIsATOMIC_SoTwoServersCannotBothGiveTheOrder()
    {
        // Two MCP clients on this machine means two servers sharing one data directory — the
        // ordinary case, not an exotic one. A read followed by a write lets both of them see an
        // unclaimed caller and both issue the order. Raised by codex in this change's plan round.
        var dir = Path.Combine(Path.GetTempPath(), "coai-callers-" + Guid.NewGuid().ToString("N")[..8]);
        var now = DateTime.UtcNow;
        var servers = Enumerable.Range(0, 8).Select(_ => new CallerSessions(dir)).ToList();

        var claims = new bool[servers.Count];
        Parallel.For(0, servers.Count, i => claims[i] = servers[i].TryClaimSplitOrder("session-a", now));

        claims.Count(c => c).Should().Be(1, "exactly one caller can be the first");
    }

    [Fact]
    public void ADifferentCaller_IsNotTheSameTask()
    {
        var callers = InATempDir(out _);
        callers.TryClaimSplitOrder("session-a", DateTime.UtcNow).Should().BeTrue();

        callers.TryClaimSplitOrder("session-b", DateTime.UtcNow).Should().BeTrue(
            "a second Claude working on something else is owed its own split order");
    }

    [Fact]
    public void AnUnwritableStore_GivesTheOrderRatherThanSwallowingIt()
    {
        // Fail OPEN, and say so. Failing closed would silently turn the whole feature off — a
        // duplicate costs one repeated instruction, the silence costs every instruction.
        var file = Path.Combine(Path.GetTempPath(), "coai-callers-file-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(file, "not a directory");
        var warnings = new List<string>();

        // A data dir that is a FILE: the `callers` directory under it can never be created.
        var claimed = new CallerSessions(file).TryClaimSplitOrder("session-a", DateTime.UtcNow, warnings.Add);

        claimed.Should().BeTrue();
        warnings.Should().ContainSingle().Which.Should().Contain("may be given again");
        File.Delete(file);
    }

    [Fact]
    public void ItSurvivesTheServer()
    {
        // The server restarts between rounds — an MCP client reconnecting respawns it — so a memory
        // held in a field would forget exactly when the epics start coming back.
        var dir = Path.Combine(Path.GetTempPath(), "coai-callers-" + Guid.NewGuid().ToString("N")[..8]);
        new CallerSessions(dir).TryClaimSplitOrder("session-a", DateTime.UtcNow).Should().BeTrue();

        new CallerSessions(dir).SplitAlreadyOrdered("session-a", DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void AfterADay_TheCallerIsANewTaskAgain()
    {
        var callers = InATempDir(out _);
        var yesterday = DateTime.UtcNow - CallerSessions.Remembers - TimeSpan.FromMinutes(1);

        callers.TryClaimSplitOrder("session-a", yesterday).Should().BeTrue();

        callers.SplitAlreadyOrdered("session-a", DateTime.UtcNow).Should().BeFalse(
            "a session long enough to span a day is a session doing more than one thing");
        callers.TryClaimSplitOrder("session-a", DateTime.UtcNow).Should().BeTrue(
            "and the expired claim is replaced rather than refusing every later one");
    }

    [Fact]
    public void AnIdWithSlashesAndColons_IsStillOneCaller()
    {
        // A session id is somebody else's string. It reached a file name unescaped once in this
        // repository already (the engine-lease slug), and the failure mode is a caller silently
        // sharing a memory with another.
        var callers = InATempDir(out _);
        var awkward = "claude/2026-09-04T10:11:12Z?x=1";

        callers.TryClaimSplitOrder(awkward, DateTime.UtcNow).Should().BeTrue();

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
    public void AClientThatSaysNothing_IsEmpty_AndTheServerFallsBackToTheCheckout() =>
        CallerIdentity.From(_ => null).Should().BeEmpty();
}
