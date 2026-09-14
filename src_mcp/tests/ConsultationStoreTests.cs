using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The consultation file: written before the launch, swept when its server is gone.</summary>
public sealed class ConsultationStoreTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-consult-store-").FullName;
    private readonly ConsultationStore _store;

    public ConsultationStoreTests() => _store = new ConsultationStore(_data);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private static ConsultationRecord Record(string status = ConsultationStatuses.Asking, string handle = "", int pid = 4242, DateTime? updated = null) =>
        new(ConsultationStore.NewId(), "caller-1", CallerIdentity.Claude, "no-session", "D:/repo", "main", "0123abc",
            "codex", "gpt-5.6", "codex", "vendorRemembers", 5, ConsultationStore.Stamp(DateTime.UtcNow))
        {
            Status = status,
            Handle = handle,
            RunnerPid = pid,
            UpdatedUtc = ConsultationStore.Stamp(updated ?? DateTime.UtcNow),
        };

    [Fact]
    public void ARecordSurvivesARoundTrip()
    {
        var written = Record(handle: "0198-aaaa");
        _store.Write(written);

        var read = _store.Read(written.Id);

        read.Should().NotBeNull();
        read!.Handle.Should().Be("0198-aaaa");
        read.Budget.Cap.Should().Be(5);
        read.Budget.Spent.Should().Be(0);
    }

    [Fact]
    public void AnIdThatIsNotOneOfOurs_IsNeverTurnedIntoAPath()
    {
        // The id arrives from a caller and becomes a file name.
        foreach (var hostile in (string[])["../../settings", "a/b", "nope", "", new string('f', 40)])
        {
            _store.Read(hostile).Should().BeNull($"'{hostile}' is not a consultation id");
        }
    }

    [Fact]
    public void AJsonFileThatIsNotAConsultation_IsNotOne()
    {
        // FOUND BY THE LIVE CHECK of story 2: the local consultant writes its answer schema, and the
        // first version of it wrote the file into this very directory — where `All()` enumerated
        // every `*.json` and handed the schema back as a record with a null id and no status. The
        // sweep would then have written and deleted files named after nothing at all. The name is the
        // guard: a consultation file is named by a consultation id, and nothing else in this
        // directory is.
        Directory.CreateDirectory(_store.Directory);
        File.WriteAllText(Path.Combine(_store.Directory, "consult-answer-schema.json"), """{"type":"object"}""");
        File.WriteAllText(Path.Combine(_store.Directory, "notes.json"), """{"id":"not-an-id","status":"open"}""");
        var real = Record();
        _store.Write(real);

        _store.All().Should().ContainSingle().Which.Id.Should().Be(real.Id);
    }

    [Fact]
    public void ASweepOverForeignFilesChangesAndDeletesNothing()
    {
        Directory.CreateDirectory(_store.Directory);
        File.WriteAllText(Path.Combine(_store.Directory, "consult-answer-schema.json"), """{"type":"object"}""");

        _store.Sweep(_ => false, DateTime.UtcNow.AddYears(1), TimeSpan.FromMinutes(15), TimeSpan.FromDays(7))
            .Should().Be(0);

        File.Exists(Path.Combine(_store.Directory, "consult-answer-schema.json")).Should().BeTrue();
    }

    [Fact]
    public void ATornFileIsNotAConsultation()
    {
        var record = Record();
        _store.Write(record);
        File.WriteAllText(_store.PathFor(record.Id), "{\"id\": \"half-writ");

        _store.Read(record.Id).Should().BeNull();
    }

    /// <summary>
    /// A well-NAMED file whose JSON carries no id is skipped — it does not take the sweep down.
    /// </summary>
    /// <remarks>
    /// The name is the guard, so such a file gets as far as being deserialised, and a missing
    /// <c>id</c> deserialises to null however non-nullable the property is — a state this store's own
    /// remark records having met live, when the local route's answer schema landed in this directory.
    /// <c>Regex.IsMatch(null)</c> throws, and `All` is what `Sweep` enumerates, so ONE such file
    /// stopped the sweep for every record behind it: interrupted consultations never reconciled,
    /// finished ones never retired. (CodeRabbit, on the pull request.)
    /// </remarks>
    [Fact]
    public void AFileWithNoIdAtAll_IsSkippedRatherThanStoppingTheSweep()
    {
        var real = Record(handle: "0198-bbbb");
        _store.Write(real);
        // Named like one of ours — 32 hex digits — and holding JSON with no `id` at all.
        File.WriteAllText(
            Path.Combine(_store.Directory, new string('a', 32) + ".json"),
            "{\"repoPath\": \"D:/repo\", \"branch\": \"main\"}");

        var act = () => _store.All();

        act.Should().NotThrow("one unreadable file must not hide every readable one");
        _store.All().Should().ContainSingle(one => one.Id == real.Id);
        _store.Sweep(_ => false, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7))
            .Should().Be(1, "the real record is still reconciled");
    }

    /// <summary>
    /// A turn is NOT orphaned while somebody still holds the repository's lock.
    /// </summary>
    /// <remarks>
    /// The pid alone was the whole test, and a pid is not a fact every server can see: a second one
    /// in another container or under another user reads the first's process as gone and writes a
    /// terminal record over a consultation that is still running — which the live card and the log
    /// then show as interrupted while the vendor is mid-answer. The lock is what both CAN see.
    /// (CodeRabbit, on the pull request.)
    /// </remarks>
    [Fact]
    public async Task ASweepLeavesAnAskingRecordAlone_WhileItsRepositoryLockIsHeld()
    {
        var repo = Directory.CreateTempSubdirectory("coai-held-").FullName;
        try
        {
            var record = Record(handle: "0198-bbbb") with { RepoPath = repo };
            _store.Write(record);

            // Somebody IS working in there — the lock the running consultation holds.
            using (await RepositoryLock.TryTakeAsync(_data, repo, TimeSpan.Zero, TestContext.Current.CancellationToken))
            {
                _store.Sweep(_ => false, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7))
                    .Should().Be(0, "a dead pid this server cannot vouch for is not a finished turn");
                _store.Read(record.Id)!.Status.Should().Be(ConsultationStatuses.Asking);
            }

            // Released, and the same sweep now reconciles it.
            _store.Sweep(_ => false, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(1);
            _store.Read(record.Id)!.Status.Should().Be(ConsultationStatuses.Interrupted);
        }
        finally
        {
            try
            {
                Directory.Delete(repo, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void ASweepFlipsAnAskingRecordWhoseServerIsGone_ToInterruptedWhenItHoldsAHandle()
    {
        // The turn may have been accepted and paid for. The handle is what makes it resumable.
        var record = Record(handle: "0198-bbbb");
        _store.Write(record);

        _store.Sweep(_ => false, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(1);

        _store.Read(record.Id)!.Status.Should().Be(ConsultationStatuses.Interrupted);
    }

    [Fact]
    public void WithNoHandle_TheSameRecordIsFailed_BecauseNothingCanBeResumed()
    {
        var record = Record();
        _store.Write(record);

        _store.Sweep(_ => false, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7));

        _store.Read(record.Id)!.Status.Should().Be(ConsultationStatuses.Failed);
    }

    [Fact]
    public void ALIVEServersConsultationIsLeftAlone()
    {
        // Two servers share one data directory as a matter of course — one per MCP client.
        var record = Record(handle: "0198-cccc");
        _store.Write(record);

        _store.Sweep(_ => true, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(0);

        _store.Read(record.Id)!.Status.Should().Be(ConsultationStatuses.Asking);
    }

    [Fact]
    public void AnOpenRecordIdlePastItsBudget_IsClosedAndItsHandleDropped()
    {
        var record = Record(ConsultationStatuses.Open, handle: "0198-dddd", updated: DateTime.UtcNow.AddMinutes(-40));
        _store.Write(record);

        _store.Sweep(_ => true, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(1);

        var swept = _store.Read(record.Id)!;
        swept.Status.Should().Be(ConsultationStatuses.Closed);
        swept.Handle.Should().BeEmpty("no zombie sessions — the vendor's conversation is let go");
        swept.Reason.Should().Contain("idle");
    }

    [Fact]
    public void AFinishedRecordPastRetention_IsDeleted()
    {
        var record = Record(ConsultationStatuses.Closed, updated: DateTime.UtcNow.AddDays(-9)) with
        {
            EndedUtc = ConsultationStore.Stamp(DateTime.UtcNow.AddDays(-9)),
        };
        _store.Write(record);

        _store.Sweep(_ => true, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(1);

        File.Exists(_store.PathFor(record.Id)).Should().BeFalse();
    }

    [Fact]
    public void AFinishedRecordInsideRetention_IsKeptForTheLog()
    {
        var record = Record(ConsultationStatuses.Closed, updated: DateTime.UtcNow.AddDays(-1)) with
        {
            EndedUtc = ConsultationStore.Stamp(DateTime.UtcNow.AddDays(-1)),
        };
        _store.Write(record);

        _store.Sweep(_ => true, DateTime.UtcNow, TimeSpan.FromMinutes(15), TimeSpan.FromDays(7)).Should().Be(0);

        File.Exists(_store.PathFor(record.Id)).Should().BeTrue();
    }
}

/// <summary>The per-caller call cap — and what it does when it cannot write its own counter.</summary>
public sealed class ConsultCallCounterTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-consult-count-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    [Fact]
    public void CallsAreCountedUntilTheCap_ThenRefused()
    {
        var counter = new ConsultCallCounter(_data);
        var now = DateTime.UtcNow;

        for (var i = 1; i <= 3; i++)
        {
            counter.TryTake("caller-a", 3, now).Should().Be(new CounterOutcome(true, i, string.Empty));
        }

        counter.TryTake("caller-a", 3, now).Allowed.Should().BeFalse();
    }

    [Fact]
    public void ASecondCallerHasItsOwnCount()
    {
        var counter = new ConsultCallCounter(_data);
        var now = DateTime.UtcNow;
        counter.TryTake("caller-a", 1, now);

        counter.TryTake("caller-b", 1, now).Allowed.Should().BeTrue();
    }

    [Fact]
    public void TheWindowExpires_AndTheCountStartsAgain()
    {
        var counter = new ConsultCallCounter(_data);
        var now = DateTime.UtcNow;
        counter.TryTake("caller-a", 1, now);

        counter.TryTake("caller-a", 1, now + ConsultCallCounter.Window + TimeSpan.FromMinutes(1)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void AnUnwritableCounter_StillENFORCESTheCap_AndSaysSo()
    {
        // NEVER fail open: the split-order claim can, because a repeated instruction is cheap; a
        // call cap that fails open is a runaway agent on a paid vendor. Two reviewers, plan round.
        var unwritable = Path.Combine(_data, "not-a-directory");
        File.WriteAllText(unwritable, "this is a file, so no directory can be made inside it");
        var counter = new ConsultCallCounter(unwritable);
        var now = DateTime.UtcNow;
        var caller = "caller-" + Guid.NewGuid().ToString("N");

        var first = counter.TryTake(caller, 2, now);
        first.Allowed.Should().BeTrue();
        first.Note.Should().Contain("in memory");

        counter.TryTake(caller, 2, now).Allowed.Should().BeTrue();
        counter.TryTake(caller, 2, now).Allowed.Should().BeFalse("the cap holds even with no file to write it in");
    }
}

/// <summary>
/// Whether two spellings name one checkout — the comparison <c>status</c> stands on.
/// </summary>
/// <remarks>
/// The two sides reach this from different places: the RECORD holds what git answered, which is the
/// real path, and the query holds whatever the session was opened with. Nothing keeps those two
/// spellings equal, and on macOS they are routinely not.
/// </remarks>
public sealed class SameCheckoutTests
{
    /// <summary>The ordinary case: one spelling, no links, still the same checkout.</summary>
    [Fact]
    public void TheSamePathSpelledTheSameWay_IsTheSameCheckout()
    {
        var path = Path.Combine(Path.GetTempPath(), "coai-one-checkout");

        ConsultationService.SamePath(path, path + Path.DirectorySeparatorChar, p => p)
            .Should().BeTrue("a trailing separator is not a different directory");
    }

    /// <summary>
    /// A checkout git reports THROUGH its links is the one the caller asked about.
    /// </summary>
    /// <remarks>
    /// <para>This is what failed on macOS, and it failed silently in the most expensive direction.
    /// <c>status</c> exists so a compacted conversation can find the consultation it already opened;
    /// the record held <c>/private/var/folders/…</c> because that is what git answers, the query
    /// held <c>/var/folders/…</c> because that is what the session was opened with, and
    /// <c>GetFullPath</c> — which normalises separators and <c>..</c> and nothing else — called them
    /// different. So <c>status</c> reported nothing open about an open consultation, and the caller
    /// did the reasonable thing with that answer: opened a second one, collected the tree again, and
    /// asked a model that had already answered, from scratch, on the same budget.</para>
    /// <para>The link is a seam rather than a real one, so this holds on every platform instead of
    /// only on the one that has a <c>/private</c>.</para>
    /// </remarks>
    [Fact]
    public void ACheckoutReachedThroughALink_IsTheSameCheckout()
    {
        var said = Path.Combine(Path.GetTempPath(), "coai-checkout-as-said");
        var real = Path.Combine(Path.GetTempPath(), "coai-checkout-as-resolved");

        string Link(string p) =>
            string.Equals(
                Path.TrimEndingDirectorySeparator(p),
                Path.TrimEndingDirectorySeparator(said),
                StringComparison.OrdinalIgnoreCase)
                ? real
                : p;

        ConsultationService.SamePath(real, said, Link)
            .Should().BeTrue("git answers with the resolved path and the session was opened with the other one");
    }

    /// <summary>
    /// One checkout is ONE owner, whichever spelling of it the caller happens to hold.
    /// </summary>
    /// <remarks>
    /// <para>When nothing in the environment names a session — a plain CLI, a runner — a consultation
    /// is owned by its checkout. That identity was built twice from two different strings: the write
    /// had already resolved the repository through git, the read used what the session was opened
    /// with. Same directory, two owners, and <c>status</c> filtered out the consultation it was
    /// asked about. It survived the first fix to <c>SamePath</c> untouched, because the path
    /// comparison was never the only thing comparing paths.</para>
    /// </remarks>
    [Fact]
    public void OneCheckoutIsOneOwner_WhicheverSpellingNamesIt()
    {
        var said = Path.Combine(Path.GetTempPath(), "coai-checkout-as-said");
        var real = Path.Combine(Path.GetTempPath(), "coai-checkout-as-resolved");

        string Link(string p) =>
            string.Equals(
                Path.TrimEndingDirectorySeparator(p),
                Path.TrimEndingDirectorySeparator(said),
                StringComparison.OrdinalIgnoreCase)
                ? real
                : p;

        ConsultationService.RepoIdentity(said, Link).Should().Be(
            ConsultationService.RepoIdentity(real, Link),
            "the session was opened with one spelling and git answered with the other");
    }

    /// <summary>Two different checkouts stay different — the guard against fixing this too hard.</summary>
    [Fact]
    public void TwoDifferentCheckouts_AreNotTheSameOne()
    {
        var one = Path.Combine(Path.GetTempPath(), "coai-checkout-one");
        var other = Path.Combine(Path.GetTempPath(), "coai-checkout-two");

        ConsultationService.SamePath(one, other, p => p).Should().BeFalse();
        ConsultationService.RepoIdentity(one, p => p).Should().NotBe(ConsultationService.RepoIdentity(other, p => p));
    }

    /// <summary>
    /// A path this machine cannot make sense of is not the one being asked about — it is not a crash.
    /// </summary>
    /// <remarks>
    /// A record can come from a server that ran on another machine. That was already true of
    /// <c>GetFullPath</c> and has to stay true now that a resolver runs over both sides.
    /// </remarks>
    [Fact]
    public void APathThisMachineCannotResolve_IsNotAMatch_AndDoesNotThrow()
    {
        var here = Path.Combine(Path.GetTempPath(), "coai-checkout-here");

        ConsultationService.SamePath(here, "\0not-a-path", p => p).Should().BeFalse();
        ConsultationService.SamePath(here, string.Empty, p => p).Should().BeFalse();
    }
}
