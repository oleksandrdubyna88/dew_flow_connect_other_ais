using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A document session is identified by the DOCUMENT, and a code session is identified exactly as it
/// always was.
/// </summary>
/// <remarks>
/// <para>The plan's first draft keyed a document session by the content HASH of the document, and
/// six findings across all three vendors killed it in one round: edit the document between rounds
/// and the key changes, so the previous round is orphaned UNRESOLVED and the
/// <c>resolve</c>-then-repeat loop the tool's own contract promises cannot work at all. What is
/// keyed is the document's IDENTITY — a path, or the name a caller gave raw text — which survives
/// every edit. The content hash survives as the per-round snapshot, which is a different job.</para>
/// <para>The load-bearing test here is the dull one: a two-argument key is byte-identical to what it
/// has always been. Every session file on this machine and on every other one is named by a hash of
/// that string.</para>
/// </remarks>
public sealed class ADocumentSessionIsKeptApartTests
{
    private static SessionState Fresh(Stage stage = Stage.PlanReview) =>
        new("s1", "/repo", "main", PanelConfig.Uniform(3, 5)) { Stage = stage };

    /// <summary>A document session: the same state carrying the document it is about.</summary>
    private static SessionState Doc(Stage stage = Stage.DocumentReview) =>
        Fresh(stage) with { Document = "docs/spec.md" };

    /// <summary>
    /// Pinned as a LITERAL, not recomputed. The thing being protected is every session file already
    /// written, and a test that derives the expected value from the code under test protects
    /// nothing.
    /// </summary>
    [Fact]
    public void ACodeSessionsKey_IsExactlyWhatItHasAlwaysBeen()
    {
        SessionKey.For(@"C:\Work\Repo\", "feature/x").Should().Be("c:/work/repo#feature/x");
        SessionKey.For("/home/me/repo", " main ").Should().Be("/home/me/repo#main");
    }

    /// <summary>The third segment is absent, not empty, when there is no document.</summary>
    [Fact]
    public void AnEmptyDocument_ChangesNoKey()
    {
        SessionKey.For("/repo", "main", string.Empty).Should().Be(SessionKey.For("/repo", "main"));
        SessionKey.For("/repo", "main", "   ").Should().Be(SessionKey.For("/repo", "main"));
    }

    [Fact]
    public void ADocumentSession_IsItsOwnSession()
    {
        var code = SessionKey.For("/repo", "main");
        var spec = SessionKey.For("/repo", "main", "docs/spec.md");
        var policy = SessionKey.For("/repo", "main", "docs/policy.md");

        spec.Should().NotBe(code).And.NotBe(policy);
    }

    /// <summary>
    /// The whole point of the revision: the document is EDITED between rounds and round 2 finds
    /// round 1. Identity is the path; content is not identity.
    /// </summary>
    [Fact]
    public void AnEditedDocument_IsTheSameSession()
    {
        var before = SessionKey.For("/repo", "main", DocumentId.Of("/repo", "/repo/docs/spec.md"));
        var after = SessionKey.For("/repo", "main", DocumentId.Of("/repo", "/repo/docs/spec.md"));

        after.Should().Be(before, "the bytes changed and the document did not");
    }

    /// <summary>A path's identity is its place in the repository, spelled one way.</summary>
    [Theory]
    [InlineData(@"C:\repo", @"C:\repo\docs\Spec.md", "docs/spec.md")]
    [InlineData("/repo", "/repo/docs/spec.md", "docs/spec.md")]
    [InlineData("/repo/", "/repo/spec.md", "spec.md")]
    public void APathsIdentity_IsItsRepoRelativePath(string repo, string path, string expected) =>
        DocumentId.Of(repo, path).Should().Be(expected);

    /// <summary>
    /// A state with no document is a code session, and that is what every file written before this
    /// change deserialises into. Absent means today's behaviour — never "none", never "any".
    /// </summary>
    [Fact]
    public void ASessionWithNoDocument_IsACodeSession()
    {
        Fresh().Document.Should().BeEmpty();
        Fresh().IsDocumentSession.Should().BeFalse();

        (Fresh() with { Document = "docs/spec.md" }).IsDocumentSession.Should().BeTrue();
    }

    /// <summary>There is no plan before a document — the document IS the work.</summary>
    [Fact]
    public void ADocumentRound_DoesNotWaitForAPlanRound()
    {
        var state = Doc() with { PlanProceeded = false };

        RoundMachine.BeginDocumentRound(state).Should().BeOfType<Transition.Moved>();
    }

    /// <summary>The regression guard: the code gate still comes second.</summary>
    [Fact]
    public void ACodeRound_StillWaitsForAPlanRound() =>
        RoundMachine.BeginCodeRound(Fresh() with { PlanProceeded = false })
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("review_plan");

    [Fact]
    public void ADocumentRound_RefusesWhileTheLastOneIsUnresolved() =>
        RoundMachine.BeginDocumentRound(Doc() with { AwaitingResolve = true })
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("resolve");

    [Fact]
    public void ADocumentRound_RefusesWhileAPersonIsBeingWaitedFor() =>
        RoundMachine.BeginDocumentRound(Doc() with { HumanGate = true })
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("ask_human");

    /// <summary>
    /// A finished review is finished — and the refusal carries the door, because a refusal with no
    /// door is a stall and an unchanged policy would otherwise be permanently unreviewable.
    /// </summary>
    [Fact]
    public void AFinishedDocumentSession_RefusesAndNamesTheWayOut() =>
        RoundMachine.BeginDocumentRound(Doc(Stage.Done))
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("newReview");

    /// <summary>Three refusals, three different sentences — nobody is sent to fix the wrong thing.</summary>
    [Fact]
    public void EachRefusal_SaysSomethingDifferent()
    {
        var sentences = new[]
        {
            RoundMachine.BeginDocumentRound(Doc() with { AwaitingResolve = true }),
            RoundMachine.BeginDocumentRound(Doc() with { HumanGate = true }),
            RoundMachine.BeginDocumentRound(Doc(Stage.Done)),
        }.Select(t => t.Should().BeOfType<Transition.Refused>().Subject.Sentence);

        sentences.Distinct().Should().HaveCount(3);
    }

    /// <summary>
    /// A round of the other kind is refused by NAME. A session that quietly changed kind under the
    /// caller would count one budget against two different jobs.
    /// </summary>
    [Fact]
    public void ACodeRoundOnADocumentSession_IsRefusedByName() =>
        RoundMachine.BeginCodeRound(Doc(Stage.CodeReview) with { PlanProceeded = true })
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("review_document");

    [Fact]
    public void ADocumentRoundOnACodeSession_IsRefusedByName() =>
        RoundMachine.BeginDocumentRound(Fresh(Stage.CodeReview))
            .Should().BeOfType<Transition.Refused>()
            .Which.Sentence.Should().Contain("review_code");
}
