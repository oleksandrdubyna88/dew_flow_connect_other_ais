using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The derivation itself: both halves of a confined launch are read off the one value, so neither
/// can be set without the other.
/// </summary>
/// <remarks>
/// This is the half of the single-policy guarantee a test can hold directly. Flip the value and both
/// views must flip; a later edit that pins either <c>Apply</c> to a constant leaves one arm of this
/// red. The other half — that <see cref="ReviewLauncher"/> reaches a request only through these two
/// views — is asserted on the launched request in <c>ReviewLauncherTests</c>, and the rest is the
/// launcher's shape: there is no second road to build one.
/// <para>Proved to have teeth on 2026-09-11 by pinning <c>Apply(ProcessRequest)</c> to
/// <c>InheritsEnvironment = false</c>: the unconfined arm failed with <i>Expected
/// open.Apply(Request).InheritsEnvironment to be True … but found False</i>, and in the same build
/// the launcher handed <c>Build</c> its settings without the policy, which turned the pair test red
/// on the argv — <i>{"Edit", "Write", "NotebookEdit"} to contain {"Bash", "Read", …}</i>. Both
/// restored, both green. Recorded here rather than in a commit message because the next reader of
/// a derivation test is the one about to call it over-engineering.</para>
/// </remarks>
public sealed class ConfinementTests
{
    private static readonly ReviewerSettings Settings = new("claude");

    private static readonly ProcessRequest Request = new("claude", [], ".");

    [Fact]
    public void BothHalvesMoveWithTheOneValue()
    {
        var confined = new Confinement(Confined: true);
        var open = new Confinement(Confined: false);

        confined.Apply(Settings).Confined.Should().BeTrue("the adapter's half of a confined launch");
        confined.Apply(Request).InheritsEnvironment.Should().BeFalse("the launcher's half of the same launch");
        open.Apply(Settings).Confined.Should().BeFalse("an unconfined launch keeps its worktree tools");
        open.Apply(Request).InheritsEnvironment.Should().BeTrue(
            "and its parent's environment, which is what the local coai-mcp runs with");
    }

    [Fact]
    public void EveryJobOnThisServerIsConfined() =>
        Confinement.OfEveryJob.Confined.Should().BeTrue(
            "a job is one employee's arbitrary prompt on a box holding every shared account, and there is no other kind");
}
