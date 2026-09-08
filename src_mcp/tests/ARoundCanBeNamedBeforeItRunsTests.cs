using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A round has a name before it runs, and that name is the only way to ask about it afterwards.
/// </summary>
/// <remarks>
/// <para>The failure these exist for: a caller dispatches a code round, the answer is lost, and it
/// comes back to ask what happened. Before this, the only handles were the last round, the round
/// number, or the session's aggregate — and every one of them can be another client's round on the
/// same repo+branch. Writing that verdict into the caller's own record is worse than having no
/// recovery, because it looks like recovery.</para>
/// <para>Real SQLite over a temp directory: the constraints ARE the feature, and a fake store would
/// prove that this test agrees with itself.</para>
/// </remarks>
public sealed class ARoundCanBeNamedBeforeItRunsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-loc-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private static readonly SubjectAttestation Subject =
        new("d:/repo/.git", "origin/main", new string('b', 40), new string('a', 40), new string('7', 40));

    /// <summary>The owner a claimed round records. Never compared against a clock.</summary>
    private static readonly RoundOwner Owner = RoundOwner.Here("test-instance");

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static RoundRecord Round(int number = 1) =>
        new("CodeReview", number, "proceed", 0, "all 3 reviewers answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-2),
        };

    private (ReservedRound Round, bool Created) Reserve(
        RoundsDb db, string token, string roundId, SubjectAttestation? subject = null) =>
        db.Reserve("s1", token, "CodeReview", "D:/repo", "feat/x", subject ?? Subject, () => roundId, out _);

    private ReservationMismatch? MismatchOf(
        RoundsDb db, string token, string roundId,
        string repoPath = "D:/repo", string branch = "feat/x", SubjectAttestation? subject = null)
    {
        db.Reserve("s1", token, "CodeReview", repoPath, branch, subject ?? Subject, () => roundId, out var mismatch);

        return mismatch;
    }

    // ---------- the locator itself ----------

    [Fact]
    public void ALocatorIsThreeParts_AndAMissingOneNamesNoRound()
    {
        new RoundLocator("coai-mcp", "s1", "r1").IsComplete.Should().BeTrue();

        // Empty is the shape a missing JSON field arrives in, and it is the one that would sail
        // through a null check and then match whatever the provider returns for "no round".
        new RoundLocator("coai-mcp", "s1", "").IsComplete.Should().BeFalse();
        new RoundLocator("coai-mcp", "", "r1").IsComplete.Should().BeFalse();
        new RoundLocator("", "s1", "r1").IsComplete.Should().BeFalse();
        new RoundLocator("coai-mcp", "s1", "   ").IsComplete.Should().BeFalse();
        new RoundLocator("coai-mcp", "s1", new string('x', 129)).IsComplete.Should().BeFalse();
    }

    [Fact]
    public void ALocatorMatchesOnAllThreeParts_NeverOnASubset()
    {
        var mine = new RoundLocator("coai-mcp", "s1", "r1");

        mine.Matches(new RoundLocator("coai-mcp", "s1", "r1")).Should().BeTrue();
        // Same session, different round: the case the round NUMBER could not tell apart.
        mine.Matches(new RoundLocator("coai-mcp", "s1", "r2")).Should().BeFalse();
        // Same ids at another server. Ids are unique only inside one namespace.
        mine.Matches(new RoundLocator("someone-else", "s1", "r1")).Should().BeFalse();
    }

    [Fact]
    public void TheSubjectHashFoldsEveryIdItAttests()
    {
        var hash = Subject.SubjectHash;

        hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
        // Deterministic: two callers computing it from the same checkout get the same string.
        Subject.SubjectHash.Should().Be(hash);

        // And every field is IN it — a hash that ignored the tree would call two different trees
        // one subject, which is the whole failure it exists to prevent.
        (Subject with { TreeSha = new string('9', 40) }).SubjectHash.Should().NotBe(hash);
        (Subject with { HeadSha = new string('9', 40) }).SubjectHash.Should().NotBe(hash);
        (Subject with { BaseSha = new string('9', 40) }).SubjectHash.Should().NotBe(hash);
        (Subject with { BaseRef = "main" }).SubjectHash.Should().NotBe(hash);
        (Subject with { RepoIdentity = "d:/other/.git" }).SubjectHash.Should().NotBe(hash);
    }

    // ---------- reserving ----------

    [Fact]
    public void ReservingRunsNoReviewerAndSpendsNoRound()
    {
        using var db = RoundsDb.Open(_dir, _log)!;

        var (reserved, created) = Reserve(db, "token-1", "r1");

        created.Should().BeTrue();
        reserved.State.Should().Be(RoundLifecycle.Reserved);
        reserved.ReadBackState.Should().Be(RoundLifecycle.NotStarted);
        reserved.Locator.ProviderId.Should().Be(RoundLocator.Provider);
        reserved.Locator.RoundId.Should().Be("r1");
        // Pinned at reservation, so the round has something to be checked against later.
        reserved.Subject.Matches(Subject).Should().BeTrue();
        reserved.RoundRef.Should().BeNull();
        reserved.ResultJson.Should().BeEmpty();
    }

    [Fact]
    public void ReservingTwiceWithOneToken_ReturnsTheSameRound_NotASecond()
    {
        using var db = RoundsDb.Open(_dir, _log)!;

        var (first, firstCreated) = Reserve(db, "token-1", "r1");
        // The caller crashed before it stored the locator and is retrying with the token it kept.
        var (second, secondCreated) = Reserve(db, "token-1", "r2-would-be-a-second-round");

        firstCreated.Should().BeTrue();
        secondCreated.Should().BeFalse();
        second.Locator.Matches(first.Locator).Should().BeTrue();
        second.Locator.RoundId.Should().Be("r1");
    }

    [Fact]
    public void TwoClientsOnOneRepoAndBranch_GetDifferentRounds()
    {
        using var db = RoundsDb.Open(_dir, _log)!;

        var (mine, _) = Reserve(db, "agent-relay-round-7", "r1");
        var (theirs, theirsCreated) = Reserve(db, "someone-elses-round", "r2");

        theirsCreated.Should().BeTrue();
        // Same session, same subject, same everything a repo+branch pair can express — and still
        // two rounds, because the locator is what tells them apart.
        mine.Locator.SessionId.Should().Be(theirs.Locator.SessionId);
        mine.Subject.SubjectHash.Should().Be(theirs.Subject.SubjectHash);
        mine.Locator.Matches(theirs.Locator).Should().BeFalse();

        // And each reads back only its own.
        db.Read(mine.Locator)!.ClientToken.Should().Be("agent-relay-round-7");
        db.Read(theirs.Locator)!.ClientToken.Should().Be("someone-elses-round");
    }

    // ---------- the crash windows ----------

    [Fact]
    public void ACrashBeforeDispatch_LeavesARoundThatProvablyRanNothing()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var (reserved, _) = Reserve(db, "token-1", "r1");

        // The caller died here. A new server reads the same file.
        using var afterRestart = RoundsDb.Open(_dir, _log)!;
        var found = afterRestart.Read(reserved.Locator)!;

        // `not_started` is the ONE answer that says nothing external was consumed, and it is only
        // ever given because the row says `reserved` — never inferred from an absence.
        found.ReadBackState.Should().Be(RoundLifecycle.NotStarted);
        found.ResultJson.Should().BeEmpty();
    }

    [Fact]
    public void ACrashAfterDispatch_ReadsBackAsRunning_NotAsNothingHavingHappened()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var (reserved, _) = Reserve(db, "token-1", "r1");
        db.Begin(reserved.Locator, Owner);

        using var afterRestart = RoundsDb.Open(_dir, _log)!;

        // The call went out. Saying "not started" here would license a second dispatch of a
        // non-idempotent call, which is the expensive half of this whole failure.
        afterRestart.Read(reserved.Locator)!.ReadBackState.Should().Be(RoundLifecycle.Running);
    }

    [Fact]
    public void OnlyAReservedRoundCanBeDispatched_SoASecondDispatchLoses()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var (reserved, _) = Reserve(db, "token-1", "r1");

        var (firstClaim, first) = db.Begin(reserved.Locator, Owner);
        firstClaim.Should().Be(RoundClaim.Claimed);
        first!.State.Should().Be(RoundLifecycle.Running);

        // THE POINT: both callers read back `running`, so the state cannot be the answer. Only the
        // call that actually moved the row is Claimed, and only Claimed may launch reviewers.
        var (secondClaim, second) = db.Begin(reserved.Locator, Owner);
        secondClaim.Should().Be(RoundClaim.AlreadyRunning);
        second!.State.Should().Be(RoundLifecycle.Running, "the state says the same thing to both");
        second.StartedUtc.Should().Be(first.StartedUtc, "and the loser changed nothing");
    }

    [Fact]
    public void OnlyOneRoundOfASessionMayRunAtATime()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var (mine, _) = Reserve(db, "token-1", "r1");
        var (theirs, _) = Reserve(db, "token-2", "r2");

        db.Begin(mine.Locator, Owner).Claim.Should().Be(RoundClaim.Claimed);

        // A DIFFERENT locator of the same session. The session file is read-modify-write and its
        // round ordinal comes from it, so two rounds in flight over one session race on the trail,
        // the worktree path and the stage state — whichever locators they carry.
        var (claim, _) = db.Begin(theirs.Locator, Owner);
        claim.Should().Be(RoundClaim.SessionBusy);
        db.Read(theirs.Locator)!.State.Should().Be(RoundLifecycle.Reserved, "and it is still spendable");

        // Once the first finishes, the slot is free.
        db.Complete(mine.Locator, null, "{}");
        db.Begin(theirs.Locator, Owner).Claim.Should().Be(RoundClaim.Claimed);
    }

    [Fact]
    public void ACompletedRoundIsClaimedByNobody_AndAnswersWithWhatItStored()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var (reserved, _) = Reserve(db, "token-1", "r1");
        db.Begin(reserved.Locator, Owner);
        db.Complete(reserved.Locator, null, "{\"verdict\":\"revise\"}");

        var (claim, round) = db.Begin(reserved.Locator, Owner);

        claim.Should().Be(RoundClaim.AlreadyCompleted);
        round!.ResultJson.Should().Contain("revise");
    }

    [Fact]
    public void ATokenNamesOneRound_SoAskingItForAnotherSubjectIsRefusedRatherThanResumed()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        Reserve(db, "token-1", "r1");

        // Same token, and each of these is a different question. Answering any of them with the
        // original pin would hand back a reservation on OTHER code as though it matched.
        MismatchOf(db, "token-1", "r2", branch: "feat/y")!.Field.Should().Be("branch");
        MismatchOf(db, "token-1", "r2", repoPath: "D:/other")!.Field.Should().Be("repoPath");
        MismatchOf(db, "token-1", "r2", subject: Subject with { BaseRef = "main" })!.Field.Should().Be("baseRef");

        var moved = Subject with { HeadSha = new string('c', 40) };
        var mismatch = MismatchOf(db, "token-1", "r2", subject: moved)!;
        mismatch.Field.Should().Be("subject");
        mismatch.Reserved.Should().Be(Subject.SubjectHash);
        mismatch.Requested.Should().Be(moved.SubjectHash);

        // The same question twice is still a resume, not a mismatch.
        MismatchOf(db, "token-1", "r2").Should().BeNull();
        db.Read(new RoundLocator(RoundLocator.Provider, "s1", "r1"))!.Subject.Matches(Subject).Should().BeTrue();
    }

    [Fact]
    public void ACompletedResultIsImmutable_ASecondCompletionCannotReplaceIt()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        db.RecordRound(Session, Round(), []);
        var roundRow = db.RoundRowId("s1", "CodeReview", 1)!.Value;
        var (reserved, _) = Reserve(db, "token-1", "r1");
        db.Begin(reserved.Locator, Owner);
        db.Complete(reserved.Locator, roundRow, "{\"verdict\":\"revise\"}");

        // A late or duplicated writer with a different answer, and a different round. The first
        // result is what every later check was made against, so the last writer must not win.
        var after = db.Complete(reserved.Locator, null, "{\"verdict\":\"proceed\"}")!;

        after.ResultJson.Should().Contain("revise");
        after.ResultJson.Should().NotContain("proceed");
        after.RoundRef.Should().Be(roundRow);
    }

    // ---------- the lost answer ----------

    [Fact]
    public void ALostAnswerIsRecoveredWhole_FindingsAndAll_AfterARestart()
    {
        var review = new ReviewAnswer(
            "revise", null, 2, 3, "all 6 reviewers answered",
            [Found("the retry never gives up"), Found("the session file opens without FileShare")],
            [], [], "resolve every finding");
        var json = System.Text.Json.JsonSerializer.Serialize(review, ServerJsonContext.Default.ReviewAnswer);

        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            var (reserved, _) = Reserve(db, "token-1", "r1");
            db.Begin(reserved.Locator, Owner);
            // ONE transaction: the round, its findings and the locator's answer. There is no
            // instant at which the round is recorded and the locator has nothing to give back.
            db.RecordRound(
                Session, Round(), review.Findings, default,
                new RoundCompletion(reserved.Locator, json));
        }

        // A different server instance, a fresh connection, the same file.
        using var afterRestart = RoundsDb.Open(_dir, _log)!;
        var found = afterRestart.Read(new RoundLocator(RoundLocator.Provider, "s1", "r1"))!;

        found.ReadBackState.Should().Be(RoundLifecycle.Completed);
        found.RoundRef.Should().NotBeNull();
        // The whole answer, not a count and not a reconstruction: the read-back and the original
        // reply are the same document.
        found.ResultJson.Should().Be(json);
        var replayed = System.Text.Json.JsonSerializer.Deserialize(found.ResultJson, ServerJsonContext.Default.ReviewAnswer)!;
        replayed.Findings.Should().HaveCount(2);
        replayed.Findings.Select(f => f.Title).Should().BeEquivalentTo(review.Findings.Select(f => f.Title));
        replayed.Verdict.Should().Be("revise");
        replayed.GatingCount.Should().Be(2);
        // And the attestation comes back identical to the one the round was reserved against.
        found.Subject.Matches(Subject).Should().BeTrue();
        found.Subject.SubjectHash.Should().Be(Subject.SubjectHash);
    }

    [Fact]
    public void ASecondRoundOfTheSameSubject_DoesNotOverwriteTheFirstsAnswer()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var (first, _) = Reserve(db, "token-1", "r1");
        var (second, _) = Reserve(db, "token-2", "r2");

        db.Begin(first.Locator, Owner);
        db.Complete(first.Locator, null, "{\"verdict\":\"revise\"}");
        db.Begin(second.Locator, Owner);
        db.Complete(second.Locator, null, "{\"verdict\":\"proceed\"}");

        // The pending list in the session file holds the LAST round's findings and nothing else.
        // This is the property that fixes: round one is still answerable after round two ran.
        db.Read(first.Locator)!.ResultJson.Should().Contain("revise");
        db.Read(second.Locator)!.ResultJson.Should().Contain("proceed");
    }

    // ---------- what a mismatched locator gets ----------

    [Fact]
    public void AnotherRoundsLocator_CannotReachThisRoundsResult()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var (mine, _) = Reserve(db, "token-1", "r1");
        Reserve(db, "token-2", "r2");
        db.Begin(mine.Locator, Owner);
        db.Complete(mine.Locator, null, "{\"verdict\":\"revise\"}");

        db.Read(new RoundLocator(RoundLocator.Provider, "s1", "r2"))!.ResultJson.Should().BeEmpty();
    }

    [Fact]
    public void AlocatorFromAnotherProvider_IsUnknownHere_NotNotStarted()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        Reserve(db, "token-1", "r1");

        // Right session, right round, wrong namespace. Session and round ids mean nothing without
        // the server they belong to.
        db.Read(new RoundLocator("some-other-server", "s1", "r1")).Should().BeNull();
        // A session that exists and a round that does not is equally unknown, and for the same
        // reason: this server cannot say, which is not the same as "nothing ran".
        db.Read(new RoundLocator(RoundLocator.Provider, "s1", "never-issued")).Should().BeNull();
        RoundLifecycle.ReadBackFor("no-such-state").Should().Be(RoundLifecycle.Unknown);
    }

    // ---------- the constraints, attacked directly ----------

    [Fact]
    public void OneLocalRoundBelongsToExactlyOneLocator()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        db.RecordRound(Session, Round(), []);
        var roundRow = db.RoundRowId("s1", "CodeReview", 1)!.Value;
        var (first, _) = Reserve(db, "token-1", "r1");
        var (second, _) = Reserve(db, "token-2", "r2");

        db.Begin(first.Locator, Owner);
        db.Complete(first.Locator, roundRow, "{}");

        // Two locators owning one round would both answer with its verdict — the duplicate the
        // whole table exists to prevent.
        db.Begin(second.Locator, Owner);
        var second_takes_the_same_round = () => db.Complete(second.Locator, roundRow, "{}");
        second_takes_the_same_round.Should().Throw<Microsoft.Data.Sqlite.SqliteException>()
            .WithMessage("*UNIQUE*");
    }

    [Fact]
    public void ACompletedRoundCannotBeReopenedOrHaveItsAttestationRewritten()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var (reserved, _) = Reserve(db, "token-1", "r1");
        db.Begin(reserved.Locator, Owner);
        db.Complete(reserved.Locator, null, "{\"verdict\":\"revise\"}");

        // Re-completing is allowed and idempotent — a retried write of the same answer must not
        // fail — but it cannot move the round back out of completed.
        db.Complete(reserved.Locator, null, "{\"verdict\":\"revise\"}");
        db.Read(reserved.Locator)!.State.Should().Be(RoundLifecycle.Completed);

        var reopen = () => db.Fail(reserved.Locator);
        reopen.Should().NotThrow("Fail excludes a completed row rather than fighting the trigger");
        db.Read(reserved.Locator)!.State.Should().Be(RoundLifecycle.Completed);
    }

    /// <summary>
    /// The attestation is fixed at reservation, and the database says so — not the one writer.
    /// </summary>
    /// <remarks>
    /// Attacked with raw SQL on a second connection, because that is the writer this constraint
    /// exists for: a future recovery path, a migration, or the panel. A rule kept only by the code
    /// that happens to call it is a rule that holds until somebody writes a second caller.
    /// </remarks>
    [Fact]
    public void WhatARoundAttestedCannotBeRewritten_NotEvenByRawSql()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        var (reserved, _) = Reserve(db, "token-1", "r1");
        db.Begin(reserved.Locator, Owner);
        db.Complete(reserved.Locator, null, "{\"verdict\":\"revise\"}");

        using var raw = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_dir, RoundsDb.FileName)};Pooling=False");
        raw.Open();

        foreach (var column in (string[])["subject_hash", "head_sha", "base_sha", "tree_sha", "round_id"])
        {
            using var write = raw.CreateCommand();
            write.CommandText = $"UPDATE round_locators SET {column} = 'rewritten' WHERE round_id = 'r1'";
            var rewrite = write.ExecuteNonQuery;
            rewrite.Should().Throw<Microsoft.Data.Sqlite.SqliteException>()
                .WithMessage("*fixed at reservation*", $"{column} is what the answer was checked against");
        }

        // A completed round cannot be walked back to an unfinished state either.
        using var reopen = raw.CreateCommand();
        reopen.CommandText = "UPDATE round_locators SET state = 'reserved' WHERE round_id = 'r1'";
        var walkBack = reopen.ExecuteNonQuery;
        walkBack.Should().Throw<Microsoft.Data.Sqlite.SqliteException>().WithMessage("*fixed at reservation*");

        db.Read(reserved.Locator)!.Subject.Matches(Subject).Should().BeTrue();
    }

    [Fact]
    public void HalfALocatorCannotBeStored()
    {
        using var db = RoundsDb.Open(_dir, _log)!;

        var empty_round_id = () => Reserve(db, "token-1", "");
        empty_round_id.Should().Throw<Microsoft.Data.Sqlite.SqliteException>().WithMessage("*CHECK*");

        var empty_token = () => Reserve(db, "", "r1");
        empty_token.Should().Throw<Microsoft.Data.Sqlite.SqliteException>().WithMessage("*CHECK*");
    }

    [Fact]
    public void ADatabaseFromTheOlderBuild_GainsTheTableWithoutLosingItsRounds()
    {
        // A file that stopped at the schema before this change, with a round already in it.
        using (var older = RoundsDb.Open(_dir, _log)!)
        {
            older.RecordRound(Session, Round(), [Found("something the older build recorded")]);
        }

        using var db = RoundsDb.Open(_dir, _log)!;
        var (reserved, _) = Reserve(db, "token-1", "r1");

        reserved.State.Should().Be(RoundLifecycle.Reserved);
        // The rounds the older build wrote are still there — the step is additive, which is what
        // `user_version` is for.
        db.RoundRowId("s1", "CodeReview", 1).Should().NotBeNull();
    }

    private static Finding Found(string title) =>
        new(Severity.Major, Category.Reliability, "src/Panel.cs", 40, title, title + " — why", "the fix", ["codex"])
        {
            Role = "SecurityReliability",
        };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp directory that will not delete is not a failed test.
        }
    }
}
