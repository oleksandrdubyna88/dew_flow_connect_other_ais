using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The review trees' root can be pointed somewhere — issue #544.
/// </summary>
/// <remarks>
/// The root is machine-local on purpose (<see cref="ReviewTreeRoot.Default"/>), and on Windows it comes from
/// the Known Folder API, which no environment variable redirects. So the extension's live contract test,
/// which runs the real binary over a temp data directory and expects no trees, read the machine's OWN
/// trees instead and failed on any machine that held one. <c>COAI_REVIEW_ROOT</c>, when it is an absolute
/// path, is the root; anything else is ignored — and said, so nobody believes a root they set is in use.
/// </remarks>
public sealed class TheReviewTreeRootCanBePointedTests
{
    private static readonly string Pointed = Path.Combine(Path.GetTempPath(), "coai-review-root-test");

    private static Func<string, string?> Env(string? value) =>
        name => name == ReviewTreeRoot.RootVariable ? value : null;

    [Fact]
    public void AnAbsoluteRoot_IsTheRoot()
    {
        var said = new List<string>();

        ReviewTreeRoot.DefaultIn(Env(Pointed), said.Add).Should().Be(Pointed,
            "the variable is how a test keeps the machine's own trees out of its answer");
        said.Should().BeEmpty();
    }

    [Fact]
    public void AnAbsoluteRoot_IsTrimmed()
    {
        ReviewTreeRoot.DefaultIn(Env($"  {Pointed}  "), _ => { }).Should().Be(Pointed);
    }

    [Theory]
    [InlineData("review-worktrees")]
    [InlineData("./trees")]
    [InlineData("   ")]
    public void ARootThatIsNotAbsolute_IsIgnored_AndSaid(string value)
    {
        var said = new List<string>();

        ReviewTreeRoot.DefaultIn(Env(value), said.Add).Should().Be(ReviewTreeRoot.MachineLocal("review-worktrees"),
            "a relative root would resolve against whatever directory the process happened to start in");
        said.Should().ContainSingle().Which.Should().Contain(ReviewTreeRoot.RootVariable)
            .And.Contain(ReviewTreeRoot.MachineLocal("review-worktrees"), "the line names the root actually in use");
    }

    [Fact]
    public void NothingSet_IsTheMachineLocalRoot_AndSaysNothing()
    {
        var said = new List<string>();

        ReviewTreeRoot.DefaultIn(Env(null), said.Add).Should().Be(ReviewTreeRoot.MachineLocal("review-worktrees"));
        said.Should().BeEmpty("the ordinary case is not a warning");
    }
}
