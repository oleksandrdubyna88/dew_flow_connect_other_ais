using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A code round is never handed a bare diff.
/// </summary>
/// <remarks>
/// <para>A reviewer given only a diff can say whether the code is defensible; it cannot say
/// whether the code is what was ASKED for. Those are different questions, and the second is the
/// one a gate exists to answer — "this is well written" and "this is what the ticket wanted" come
/// apart constantly, and only the second catches a change that solved the wrong problem
/// beautifully.</para>
/// <para>Nothing enforced it: <c>review_code</c> took <c>planText</c> as an ordinary argument, and
/// an empty string was accepted in silence. The scope a plan round already agreed on was not even
/// kept — so the one place the intent was written down was thrown away between the two stages.</para>
/// </remarks>
public sealed class CodeScopeTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-scope-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private PanelService Service() =>
        new(new PanelSettings { DataDir = _data }, VaultKeys.None("not configured for this test"), default, new ProcessLauncher(), Serilog.Core.Logger.None);

    private const string Scope = """
        # SCOPE — the retry must not lose the reviewer's own words

        A reviewer that fails is recorded with `outcome` and nothing else, so the round summary
        says "unparseable" without the text that would not parse. Keep the raw answer beside the
        session and name the file in the refusal, so a person can read what was actually returned.
        """;

    [Fact]
    public async Task ACodeRound_WithNoScope_IsRefused_NamingWhatToPass()
    {
        // The refusal only applies once the stage is reachable: "the plan stage has not passed" is
        // the more useful sentence for a caller who skipped it, so a session that HAS passed is
        // what puts the scope rule in play.
        var passed = new SessionState("s", "D:/nowhere", "main", new PanelConfig()) with
        {
            Stage = Stage.CodeReview,
            PlanProceeded = true,
        };
        new SessionStore(_data).Save(new PersistedSession(passed, []));

        var service = Service();
        var answer = await service.ReviewCodeAsync("D:/nowhere", "main", "main~1", planText: "  ");

        answer.Should().ContainEquivalentOf("scope", "the refusal has to say what is missing");
        answer.Should().NotContain("worktree", "nothing should have been launched to discover this");
    }

    [Fact]
    public void ThePlanThatPassed_IsTheScope_AndIsKept()
    {
        // The plan round already carries the intent, agreed by both halves. Throwing it away and
        // then asking the caller to send it again is how a caller ends up sending nothing.
        var store = new SessionStore(_data);
        var session = new PersistedSession(new SessionState("s", "D:/r", "main", new PanelConfig()), []) { PlanText = Scope };
        store.Save(session);

        store.Load("D:/r", "main")!.PlanText.Should().Be(Scope);
    }

    [Fact]
    public void AOneLineTicketTitle_IsNotAScope()
    {
        // "fix the update button" passes any is-it-empty check and tells a reviewer nothing about
        // what the change was supposed to achieve, which is the whole question it is being asked.
        CodeScope.IsSubstantial("fix the update button").Should().BeFalse();
        CodeScope.IsSubstantial(Scope).Should().BeTrue();
    }
}
