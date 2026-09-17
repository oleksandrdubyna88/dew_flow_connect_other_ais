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

    /// <summary>
    /// A record file written by an OLDER build comes back with empty strings, never nulls.
    /// </summary>
    /// <remarks>
    /// <para>The source-generated deserializer skips property INITIALISERS for members the JSON
    /// does not carry — the <c>PersistedSession</c> precedent this record already names for its
    /// collections. Every string on it had the same hole: a file from before a field existed reads
    /// back null, and the first <c>.Length</c> anywhere downstream throws.</para>
    /// <para>Found by the <c>--close-consult</c> scenario, which crashed with a
    /// <c>NullReferenceException</c> on a record whose <c>reason</c> the file simply did not have —
    /// which is every record written before issue #309. (issue #309.)</para>
    /// </remarks>
    [Fact]
    public void ARecordFromAnOlderBuild_ReadsBackWithEmptyStringsRatherThanNulls()
    {
        var store = new ConsultationStore(_data);
        var id = ConsultationStore.NewId();
        Directory.CreateDirectory(store.Directory);
        // Exactly the members an older build wrote, and not one more.
        File.WriteAllText(store.PathFor(id), $$"""
            {
              "id": "{{id}}",
              "caller": "repo:somewhere",
              "callerKind": "claude",
              "sessionId": "s1",
              "repoPath": "D:/repo",
              "branch": "main",
              "headSha": "sha",
              "vendor": "codex",
              "model": "gpt-6",
              "runtime": "codex",
              "memory": "vendorRemembers",
              "maxTurns": 5,
              "startedUtc": "2026-09-17T09:00:00.0000000Z",
              "status": "open"
            }
            """);

        var record = store.Read(id)!;

        record.Reason.Should().NotBeNull("a missing member must not come back as null");
        record.EndedUtc.Should().NotBeNull();
        record.UpdatedUtc.Should().NotBeNull();
        record.Outcome.Should().NotBeNull();
        record.Handle.Should().NotBeNull();
        record.Alert.Should().NotBeNull();
        // And the one thing anybody does with them does not throw.
        var lengths = () => record.Reason.Length + record.EndedUtc.Length + record.Outcome.Length
            + record.UpdatedUtc.Length + record.Handle.Length + record.Alert.Length;
        lengths.Should().NotThrow();
    }
}
