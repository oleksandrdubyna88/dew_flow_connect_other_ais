using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
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

    /// <summary>
    /// A path's identity is its place in the repository — forward slashes, and its OWN case.
    /// </summary>
    /// <remarks>
    /// It was lower-cased, and two reviewers found the same hole from opposite ends: on a
    /// case-sensitive filesystem that merges two genuinely distinct files into one session, and — the
    /// security half — it makes a SIBLING directory look contained. The two tests below are each of
    /// those.
    /// </remarks>
    [Theory]
    [InlineData(@"C:\repo", @"C:\repo\docs\Spec.md", "docs/Spec.md")]
    [InlineData("/repo", "/repo/docs/spec.md", "docs/spec.md")]
    [InlineData("/repo/", "/repo/spec.md", "spec.md")]
    public void APathsIdentity_IsItsRepoRelativePath(string repo, string path, string expected) =>
        DocumentId.Of(repo, path).Should().Be(expected);

    /// <summary>
    /// A sibling directory differing only in CASE is not inside, where the disk says it is not.
    /// </summary>
    /// <remarks>
    /// Both sides were lower-cased, so on Linux <c>/tmp/Repo/secrets.md</c> passed containment
    /// against <c>/tmp/repo</c> as <c>repo/secrets.md</c> — read, and sent to three vendors. codex
    /// called it Blocking; gemini found the same root cause from the identity end.
    /// </remarks>
    [Fact]
    public void ACaseDifferentSibling_IsNotInsideWhereTheDiskSaysItIsNot()
    {
        var inside = DocumentId.Of("/tmp/repo", "/tmp/Repo/secrets.md");

        if (OperatingSystem.IsWindows())
        {
            inside.Should().Be("secrets.md", "Windows really does treat those as one directory");
        }
        else
        {
            inside.Should().BeEmpty("/tmp/Repo and /tmp/repo are two directories on this filesystem");
        }
    }

    /// <summary>Two files differing only in case are two documents on a filesystem that agrees.</summary>
    [Fact]
    public void TwoFilesDifferingOnlyInCase_KeepTheirOwnIdentities()
    {
        DocumentId.Of("/repo", "/repo/Spec.md").Should().NotBe(DocumentId.Of("/repo", "/repo/spec.md"),
            "the identity keeps the case it was given, so a case-sensitive checkout gets two reviews");
    }

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

    // ---------- what the code round found (2026-09-13) ----------

    /// <summary>
    /// A document whose own name ends in an ordinal cannot be keyed by it.
    /// </summary>
    /// <remarks>
    /// <c>#</c> is an ordinary character in a filename, so a real <c>notes#2.md</c> and the second
    /// review of <c>notes.md</c> would share an identity — one person's round landing in another's
    /// session. Refused by name rather than escaped: an escape scheme is a second thing to get
    /// right. (codex, the code round.)
    /// </remarks>
    [Theory]
    [InlineData("docs/notes#2.md#2")]
    [InlineData("Spec#10")]
    public void AnIdentityThatLooksLikeAnOrdinal_IsRefused(string document) =>
        DocumentSessions.Reserved(document).Should().NotBeNull().And.Subject.Should().Contain(document);

    [Theory]
    [InlineData("docs/notes#2.md")]
    [InlineData("docs/spec.md")]
    [InlineData("Spec")]
    public void AnOrdinaryIdentity_IsNotReserved(string document) =>
        DocumentSessions.Reserved(document).Should().BeNull();

    /// <summary>The ordinal walk asks only whether a file is there, never what is in it.</summary>
    [Fact]
    public void TheOrdinalWalk_StopsAtTheFirstFreeName()
    {
        var taken = new HashSet<string>(["spec.md", "spec.md#2"], StringComparer.Ordinal);

        DocumentSessions.Which("spec.md", newReview: false, taken.Contains).Should().Be("spec.md#2");
        DocumentSessions.Which("spec.md", newReview: true, taken.Contains).Should().Be("spec.md#3");
        DocumentSessions.Which("other.md", newReview: true, taken.Contains).Should().Be("other.md",
            "a first review and a fresh one are the same act when there is nothing to keep apart from");
    }

    /// <summary>
    /// One file the filesystem calls one file is ONE session, however a caller spelled it.
    /// </summary>
    /// <remarks>
    /// The gap between the comparison this gained on round 1 and the key, which was still a raw
    /// string: on Windows a person who typed <c>DOCS\SPEC.MD</c> after reviewing <c>docs/spec.md</c>
    /// got a second session for a document already under review — <c>status</c> missing the open
    /// round, and a duplicate review starting. (codex, round 2.)
    /// </remarks>
    [Fact]
    public void OneFileIsOneSession_HoweverItWasSpelled()
    {
        var typed = SessionKey.For("/repo", "main", "docs/spec.md");
        var shouted = SessionKey.For("/repo", "main", "DOCS/SPEC.MD");

        if (OperatingSystem.IsWindows())
        {
            shouted.Should().Be(typed, "Windows calls those one file, so they are one review");
        }
        else
        {
            shouted.Should().NotBe(typed, "here they are two files, so they are two reviews");
        }
    }

    /// <summary>
    /// A stage nobody mapped to a bucket is a defect, not a plan round.
    /// </summary>
    /// <remarks>
    /// It had a discard arm, and codex named what that costs: a stage added to the enum and
    /// forgotten here would select the plan roster and the plan vendor switch with no compile error
    /// and no exception — a round quietly asking the wrong reviewers.
    /// </remarks>
    [Fact]
    public void AStageWithNoBucket_Throws()
    {
        var act = () => PanelConfig.BucketFor((Stage)999);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*map it here*");
    }

    /// <summary>And every stage that IS in the enum answers, including the finished one.</summary>
    [Fact]
    public void EveryRealStage_HasABucket()
    {
        foreach (var stage in Enum.GetValues<Stage>())
        {
            PanelConfig.BucketFor(stage).Should().NotBeNull($"{stage} must choose a roster");
        }
    }
}
