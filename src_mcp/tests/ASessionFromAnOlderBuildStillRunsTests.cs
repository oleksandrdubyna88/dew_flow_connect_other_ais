using System.Text.Json;
using Xunit;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A session file written before a field existed still runs a round.
/// </summary>
/// <remarks>
/// <para>Reported 2026-09-06 as "coai mcp is dead": every `review_code` in every repository threw
/// <c>Value cannot be null. (Parameter 'first')</c> while `review_plan`, `status`, `resolve`, `open`
/// and `providers` all answered normally. The stack named
/// <c>Enumerable.Union</c> inside <c>RunStageAsync</c> — an extension method called on a null
/// receiver, which is `session.UsedPrompts` at the line that records which lenses a round spent.</para>
/// <para>The cause is data, not code: `UsedPrompts` arrived on 2026-09-01, and a session file
/// written before that has no such member. The property carries <c>= []</c>, and that initializer is
/// what nobody could rely on — so an old file loads with a null list and the NEXT round over that
/// session dies. New sessions were fine, which is why the plan stage looked healthy: it had just
/// created one.</para>
/// <para>Kept as a rule rather than a patch: a collection that came off disk is normalised where it
/// is declared, so no caller anywhere has to remember which build wrote the file it is reading.</para>
/// </remarks>
public sealed class ASessionFromAnOlderBuildStillRunsTests
{
    /// <summary>Exactly the shape found on disk: no `usedPrompts` member at all.</summary>
    private const string BeforeUsedPromptsExisted = """
        {
          "state": {
            "sessionId": "924f8ea0",
            "repoPath": "D:/rsd/dew_flow_connect_other_ais",
            "branch": "main",
            "stage": "CodeReview",
            "awaitingResolve": false
          },
          "rounds": [],
          "openedUtc": "2026-09-01T08:00:00Z",
          "pending": [],
          "planText": "SCOPE - something agreed at the plan stage"
        }
        """;

    [Fact]
    public void ASessionWrittenBeforeTheFieldExisted_HasAnEmptyListRatherThanNull()
    {
        var session = JsonSerializer.Deserialize(
            BeforeUsedPromptsExisted, ServerJsonContext.Default.PersistedSession);

        session.Should().NotBeNull();
        session!.UsedPrompts.Should().NotBeNull(
            "a null here killed every review_code in every repository on 2026-09-06");
        session.UsedPrompts.Should().BeEmpty();
    }

    [Fact]
    public void AndTheLineThatKilledIt_Runs()
    {
        // The exact expression from RunStageAsync, which is where the exception came from. Asserted
        // rather than described, because the value that broke it was legal JSON.
        var session = JsonSerializer.Deserialize(
            BeforeUsedPromptsExisted, ServerJsonContext.Default.PersistedSession)!;

        var spent = () => session.UsedPrompts.Union(["universal", ""]).Where(p => p.Length > 0).ToList();

        spent.Should().NotThrow().Which.Should().ContainSingle().Which.Should().Be("universal");
    }

    [Fact]
    public void AnExplicitNullIsAlsoAnEmptyList()
    {
        // Once one build had written a null — and one had, because a null list serialises as null —
        // every later build read it back. The normalisation has to cover the value as well as the
        // absence, or the bad files stay bad for ever.
        var withNulls = """
            {
              "state": { "sessionId": "s", "repoPath": "D:/x", "branch": "main", "stage": "PlanReview" },
              "rounds": [],
              "usedPrompts": null,
              "pending": null
            }
            """;

        var session = JsonSerializer.Deserialize(withNulls, ServerJsonContext.Default.PersistedSession)!;

        session.UsedPrompts.Should().NotBeNull().And.BeEmpty();
        session.Pending.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ASessionWithNoRoundsMember_IsNotANullRoundList()
    {
        // `Rounds` is a CONSTRUCTOR parameter, so an absent member passes null straight in and no
        // property initializer can catch it — the same defect one layer over, and this one would take
        // down the panel rather than a round. It can only be normalised where a session comes off
        // disk, so this goes through the store rather than through the serializer.
        var dir = Directory.CreateTempSubdirectory("coai-oldsession-").FullName;
        var store = new SessionStore(dir);
        var withoutRounds = """
            {
              "state": { "sessionId": "s", "repoPath": "D:/x", "branch": "main", "stage": "PlanReview" },
              "openedUtc": "2026-09-01T08:00:00Z"
            }
            """;
        var file = store.FileFor("D:/x", "main");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, withoutRounds);

        var session = store.Load("D:/x", "main");

        session.Should().NotBeNull();
        session!.Rounds.Should().NotBeNull().And.BeEmpty();
    }
}
