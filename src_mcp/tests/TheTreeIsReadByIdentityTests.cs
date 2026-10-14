using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The two decisions <see cref="ProcessTree"/> makes from the process table, on tables no machine could be made to
/// produce on demand: a child listed under a REUSED parent number, and a zombie leader whose other threads still run
/// (the review of epic 6's Linux fix, <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
public sealed class TheTreeIsReadByIdentityTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A table records a parent NUMBER, not a parent. A process whose parent died, under a number since reused by a
    /// reader, is listed beneath that reader — and the reconcile would kill somebody else's process. A child cannot
    /// start before its parent, so one that did is not its child.
    /// </summary>
    [Fact]
    public void AChildOlderThanItsParent_IsNotItsChild_NorAreItsOwn()
    {
        var parents = new Dictionary<int, int> { [10] = 1, [11] = 10, [20] = 1, [21] = 20 };
        var started = new Dictionary<int, DateTime>
        {
            [1] = Start,
            [10] = Start.AddSeconds(1),
            [11] = Start.AddSeconds(2),
            [20] = Start.AddHours(-1),
            [21] = Start.AddSeconds(3),
        };

        var found = ProcessTree.Walk(1, parents, pid => started.TryGetValue(pid, out var at) ? at : null);

        found.Select(member => member.Pid).Should().BeEquivalentTo(
            [10, 11], "20 started an hour before the reader that now holds its parent's number, and 21 is 20's");
    }

    [Fact]
    public void ARootThatIsGone_HasNoDescendants()
    {
        var parents = new Dictionary<int, int> { [10] = 1 };

        ProcessTree.Walk(1, parents, pid => pid == 10 ? Start : null).Should().BeEmpty(
            "with no start time for the root there is nothing to tell its children from a reused number's");
    }

    /// <summary>
    /// On Linux the thread-group leader reads <c>Z</c> once its own thread has exited, while the group's other threads
    /// still run and still hold what the process held. Only a group with no thread but the leader has exited.
    /// </summary>
    [Theory]
    [InlineData("Z", new[] { 5 }, true)]
    [InlineData("Z", new int[0], true)]
    [InlineData("X", new[] { 5 }, true)]
    [InlineData("Z", new[] { 5, 6 }, false)]
    [InlineData("S", new[] { 5 }, false)]
    [InlineData("R", new int[0], false)]
    public void AnEntryHasExited_OnlyWhenNoOtherThreadOfItsGroupRuns(string state, int[] threads, bool exited)
    {
        ProcessTree.ExitedEntry(state, threads, pid: 5).Should().Be(exited);
    }
}
