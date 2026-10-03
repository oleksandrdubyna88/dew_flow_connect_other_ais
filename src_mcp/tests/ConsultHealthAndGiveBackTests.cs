using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Files;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The pieces epic 2 added beneath the consultation: the bounded, fenced give-back of a call; the caller
/// kind's health files and the one rule that reads them; the bounded evidence file; and a running bill that
/// loses no field.
/// </summary>
/// <remarks>PLAN_the_consultant_works_on_every_vendor.md, E2.2, E2.3 and E2.5.</remarks>
public sealed class ConsultHealthAndGiveBackTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-consult-health-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static readonly DateTime Now = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    // ---------- the give-back ----------

    [Fact]
    public void AGiveBack_ReturnsExactlyTheCallItTook()
    {
        var counter = new ConsultCallCounter(_data);
        var take = counter.TryTake("caller-1", 1, Now).Take;

        counter.TryTake("caller-1", 1, Now).Allowed.Should().BeFalse("the cap of one is spent");
        counter.GiveBack(take, Now).Should().BeTrue();
        counter.TryTake("caller-1", 1, Now).Allowed.Should().BeTrue("the call came back");
    }

    [Fact]
    public void AGiveBack_IsRefusedOnceTheWindowRolledOver()
    {
        var counter = new ConsultCallCounter(_data);
        var take = counter.TryTake("caller-1", 5, Now).Take;
        var later = Now + ConsultCallCounter.Window + TimeSpan.FromMinutes(1);
        counter.TryTake("caller-1", 5, later).Allowed.Should().BeTrue("a fresh window");

        counter.GiveBack(take, later).Should().BeFalse("the take belongs to a window that is gone — refunding it would grow the new one");
    }

    [Fact]
    public void AGiveBack_IsBoundedPerWindow()
    {
        var counter = new ConsultCallCounter(_data);
        var given = Enumerable.Range(0, ConsultCallCounter.GivesBackPerWindow + 1)
            .Select(_ => counter.GiveBack(counter.TryTake("caller-1", 10, Now).Take, Now))
            .ToList();

        given.Should().Equal(true, true, true, false);
    }

    [Fact]
    public void AGiveBack_NeverCrossesStores()
    {
        var counter = new ConsultCallCounter(_data);
        var fromFile = counter.TryTake("caller-1", 10, Now).Take;

        counter.GiveBack(fromFile with { Store = CounterStore.Memory }, Now).Should().BeFalse("memory never counted this call");
        counter.GiveBack(CounterTake.Nothing, Now).Should().BeFalse("nothing taken, nothing to give back");
        counter.GiveBack(fromFile, Now).Should().BeTrue("and the file, which did, takes it back");
    }

    [Fact]
    public void ACounterFileFromBeforeTheGiveBack_ReadsAsNoneGivenBack()
    {
        var counter = new ConsultCallCounter(_data);
        var take = counter.TryTake("caller-1", 10, Now).Take;
        var file = Directory.EnumerateFiles(Path.Combine(_data, "consultations", "callers")).Single();
        File.WriteAllText(file, $"{take.WindowStart:o}\t4\tcaller-1\n");

        counter.GiveBack(take, Now).Should().BeTrue();
        File.ReadAllText(file).Split('\t')[1..3].Should().Equal("3", "1");
    }

    private string CounterFile(string caller) =>
        Path.Combine(_data, "consultations", "callers",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(caller))) + ".txt");

    [Theory]
    [InlineData("")]
    [InlineData("2026-10-02T11:")]
    public void ACounterFileThatExistsButDoesNotParse_IsTheCapReached_NotAFreshWindow(string torn)
    {
        // A truncated write leaves a file that reads as nothing; counting it as a fresh window is the cap
        // failing open — every call it already spent handed back at once.
        Directory.CreateDirectory(Path.Combine(_data, "consultations", "callers"));
        File.WriteAllText(CounterFile("caller-1"), torn);

        var refused = new ConsultCallCounter(_data).TryTake("caller-1", 5, Now);

        refused.Allowed.Should().BeFalse();
        refused.Note.Should().Contain(CounterFile("caller-1"), "the sentence names the file a person has to look at");
    }

    [Fact]
    public void ACounterFileThatCannotBeRead_IsTheCapReached_NotAFreshWindow()
    {
        var counter = new ConsultCallCounter(_data);
        counter.TryTake("caller-1", 5, Now).Allowed.Should().BeTrue();
        using var held = new FileStream(CounterFile("caller-1"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var blocked = new ConsultCallCounter(_data).TryTake("caller-1", 5, Now);

        blocked.Allowed.Should().BeFalse("a file nobody can read says nothing about what was spent, and the cap fails closed");
        blocked.Note.Should().Contain(CounterFile("caller-1"));
    }

    [Fact]
    public void AGiveBackWithNoCounterFile_IsRefusedAtOnce()
    {
        var take = new CounterTake("caller-nobody-counted", Now, CounterStore.File);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        new ConsultCallCounter(_data).GiveBack(take, Now).Should().BeFalse();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "a missing file is not contention: there is nothing to wait for");
    }

    [Fact]
    public void ATake_LeavesExactlyOneLine()
    {
        var counter = new ConsultCallCounter(_data);
        counter.TryTake("caller-1", 5, Now);
        counter.TryTake("caller-1", 5, Now);

        File.ReadAllText(CounterFile("caller-1")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle();
    }

    // ---------- the health files ----------

    [Fact]
    public void AFailureIsCurrent_OnlyWhenItIsLaterThanTheLastAnswer()
    {
        var row = new ConsultantIdentity("codex", "gpt-5.6");
        HealthOnDisk<ConsultHealthAnswer> answer = new HealthOnDisk<ConsultHealthAnswer>.Found(new ConsultHealthAnswer { Utc = "2026-10-02T12:00:00.0000000Z", Vendor = "codex", Model = "gpt-5.6" });
        HealthOnDisk<ConsultHealthFailure> earlier = new HealthOnDisk<ConsultHealthFailure>.Found(new ConsultHealthFailure { Utc = "2026-10-02T11:00:00.0000000Z", Vendor = "codex", Model = "gpt-5.6" });
        HealthOnDisk<ConsultHealthFailure> later = new HealthOnDisk<ConsultHealthFailure>.Found(new ConsultHealthFailure { Utc = "2026-10-02T13:00:00.0000000Z", Vendor = "codex", Model = "gpt-5.6" });

        ConsultHealth.Current(answer, later, row).Should().BeTrue();
        ConsultHealth.Current(answer, earlier, row).Should().BeFalse("it answered since");
        ConsultHealth.Current(new HealthOnDisk<ConsultHealthAnswer>.None(), earlier, row).Should().BeTrue("nothing has answered at all");
        ConsultHealth.Current(new HealthOnDisk<ConsultHealthAnswer>.Unreadable("torn"), earlier, row).Should().BeTrue("an answer nobody can read clears nothing");
        ConsultHealth.Current(answer, new HealthOnDisk<ConsultHealthFailure>.None(), row).Should().BeFalse();
    }

    [Fact]
    public void AHealthFile_IsOnlyEverReplacedByANewerOne()
    {
        var store = new ConsultHealthStore(_data, _ => { });
        store.Failed(new ConsultHealthFailure { CallerKind = "claude", Utc = "2026-10-02T13:00:00.0000000Z", Kind = "quota" });

        store.Failed(new ConsultHealthFailure { CallerKind = "claude", Utc = "2026-10-02T12:00:00.0000000Z", Kind = "empty" });

        store.LastFailure("claude").ValueOrNull!.Kind.Should().Be("quota", "a slow server finishing late must not overwrite a newer outcome");
        store.Failed(new ConsultHealthFailure { CallerKind = "claude", Utc = "2026-10-02T14:00:00.0000000Z", Kind = "exit" });
        store.LastFailure("claude").ValueOrNull!.Kind.Should().Be("exit");
        Directory.EnumerateFiles(store.Directory, "*.tmp").Should().BeEmpty("every write moved its temporary over the file");
    }

    [Fact]
    public void TheHealthDirectory_IsNoConsultationToTheStore()
    {
        new ConsultHealthStore(_data, _ => { }).Answered(new ConsultHealthAnswer { CallerKind = "claude", Utc = "2026-10-02T13:00:00.0000000Z" });

        new ConsultationStore(_data).All().Should().BeEmpty("health/ holds caller kinds' files, not consultations");
        File.Exists(Path.Combine(_data, "consultations", "health", "claude.answer.json")).Should().BeTrue("the file it ignores is there");
    }

    // ---------- the evidence ----------

    [Fact]
    public void EvidenceIsBounded_AndNeverLeftHalfWritten()
    {
        var dir = Path.Combine(_data, "unparseable");

        var kept = EvidenceFile.Keep(dir, "consult-x.txt", new string('x', EvidenceFile.Cap + 10_000), _ => { });

        kept.Should().NotBeNull();
        File.ReadAllText(kept!).Should().StartWith(new string('x', 100)).And.EndWith($"[truncated at {EvidenceFile.Cap} characters]");
        new FileInfo(kept!).Length.Should().BeLessThan(EvidenceFile.Cap + 200);
        Directory.EnumerateFiles(dir, "*.writing").Should().BeEmpty();
    }

    // ---------- the bill ----------

    private static ConsultationRecord Record() =>
        new("c0ffee00", "caller", "claude", "no-session", "/repo", "main", "0123abc", "antigravity", "m", "antigravity", "vendorRemembers", 5, "2026-10-02T10:00:00.0000000Z");

    [Fact]
    public void AShareCarriesEveryFieldTheLedgerKeeps()
    {
        var billed = Record() with { Billed = new ConsultationBilled(100, 10, 0, 40, 5) };

        var share = ConsultationBilling.ThisTurnsShare(true, billed, new Usage(250, 25, null, TokensCached: 90, NoPriceSet: true, TokensReasoning: 12, NotCaptured: true));

        share.Should().Be(new Usage(150, 15, null, TokensCached: 50, NoPriceSet: true, TokensReasoning: 7, NotCaptured: true));
    }

    [Fact]
    public void ARecordFromBeforeTheRunningTotal_HasBeenBilledWhatItsTurnsSay()
    {
        var old = Record() with { Turns = [new ConsultationTurn("2026-10-02T10:01:00.0000000Z", "p", "a", 1, 300, 30, 0.2)] };

        old.Billed.Should().Be(new ConsultationBilled(300, 30, 0.2));
        Record().Billed.Should().Be(new ConsultationBilled(), "a consultation with no turns has been billed nothing");
    }
}
