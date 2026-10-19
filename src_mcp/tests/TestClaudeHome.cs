using CoaiMcp.Store;
using Xunit;

[assembly: AssemblyFixture(typeof(CoaiMcp.Tests.TestClaudeHome))]

namespace CoaiMcp.Tests;

/// <summary>
/// The whole run reads an EMPTY Claude home, never the developer's (parent §16 D17).
/// </summary>
/// <remarks>
/// <para>Without it, <c>ClaudeHome.Default</c> — and so <c>AgentLog.DefaultProjectsDir</c>, which the FakeCli round tests reach through
/// <c>RoundEngine</c> — names <c>~/.claude/projects</c>, and a test run opens a person's real transcripts. The seam is ONE method,
/// <c>ClaudeHome.ReplaceDefault</c>; the process's profile variables are never rewritten, because child processes the suite starts
/// would inherit that too.</para>
/// <para>The folder carries the <c>testsToDelete-</c> prefix (the family's <c>test-leftovers</c> rule) and is removed with the
/// fixture. E5.S4 adds only the clearing of the caller-identity variables on top of this.</para>
/// </remarks>
public sealed class TestClaudeHome : IDisposable
{
    private readonly TempDir _root = TempDir.For("testsToDelete-coai-claudehome-");
    private readonly ClaudeHome _before = ClaudeHome.Default;

    public TestClaudeHome()
    {
        Directory.CreateDirectory(_root.At("projects"));
        Directory.CreateDirectory(_root.At("sessions"));
        ClaudeHome.ReplaceDefault(new ClaudeHome(_root.At("projects"), _root.At("sessions")));
    }

    public void Dispose()
    {
        ClaudeHome.ReplaceDefault(_before);
        _root.Dispose();
    }
}
