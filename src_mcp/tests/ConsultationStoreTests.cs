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
    public void ATornFileIsNotAConsultation()
    {
        var record = Record();
        _store.Write(record);
        File.WriteAllText(_store.PathFor(record.Id), "{\"id\": \"half-writ");

        _store.Read(record.Id).Should().BeNull();
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
