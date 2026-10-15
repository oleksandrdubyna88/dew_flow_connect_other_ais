using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Snapshots;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The conversation's place (<see cref="ConsultantPlaces.ForConversationAsync"/>) and its folder's stamp, as the epic-3 code
/// round of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c> found them: a runtime that declares a homes folder gets an owned,
/// stamped, locked home whether or not it asks for a copy; a turn cancelled while it waits for the folder's lock gives its
/// in-process turn back; and a stamp nobody can read is no owner — never an exception out of the sweep.
/// </summary>
public sealed class AConversationPlaceTests : IDisposable
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromMinutes(12);

    private readonly TempDir _dir = TempDir.For("coai-answers-place-");

    private string Homes => _dir.At("local");

    private string Store => ConversationFolder.StoreOf(_dir.At("data"));

    public void Dispose() => _dir.Dispose();

    /// <summary>A runtime that keeps a coai-owned home and asks for NO copy — the shape a Kimi consultant may take.</summary>
    private sealed class HomeOnly : IConsultantRuntime
    {
        public string Vendor => "home-only";

        public ConsultantMemory Memory => new ConsultantMemory.VendorRemembers();

        public string HomesFolder => ConsultantHomes.Kimi;

        public ReviewerInvocation Build(ConsultantLaunch launch) => throw new NotSupportedException("never launched here");

        public string ReadHandle(ProcessResult result) => string.Empty;

        public bool DroppedTheConversation(ProcessResult result) => false;
    }

    private ConsultantPlaces Places() => new(Homes, Store, new ProcessLauncher(), _ => { });

    [Fact]
    public async Task ARuntimeThatDeclaresAHomesFolderWithoutACopy_GetsAnOwnedStampedLockedHome_InTheCheckout()
    {
        var id = ConsultationStore.NewId();
        var folder = ConsultantHomes.ConversationFolder(Homes, ConsultantHomes.Kimi, id);

        var place = await Places().ForConversationAsync(new HomeOnly(), id, _dir.At("repo"), Ceiling, TestContext.Current.CancellationToken);

        using (place)
        {
            place.Refusal.Should().BeEmpty();
            place.Folder.Should().Be(folder, "its home is the conversation's own folder, owned like a copying runtime's");
            place.Workdir.Should().BeEmpty("it asked for no copy, so it stands where it always stood");
            ConversationFolder.Read(folder).Store.Should().Be(Store, "the folder is stamped with the store that made it");
            ConversationFolder.TakeTurn(folder).Should().BeOfType<Hold.Busy>("the turn lock is held for the whole turn");
        }

        ConversationFolder.Read(folder).Ended.Should().BeTrue("the stamp is ended when the place is given back");
        VibeHomeTurns.Held(folder).Should().BeFalse("and so is the in-process turn");
    }

    [Fact]
    public async Task ACancellationWhileTheTurnWaitsForTheFoldersLock_GivesTheInProcessTurnBack()
    {
        var id = ConsultationStore.NewId();
        var folder = ConsultantHomes.ConversationFolder(Homes, ConsultantHomes.Vibe, id);
        var places = Places();
        places.TakingTheLock = (_, _) => throw new OperationCanceledException("the caller withdrew while the lock was awaited");

        var taking = () => places.ForConversationAsync(new VibeConsultant(new VibeRuntime()), id, _dir.At("repo"), Ceiling, TestContext.Current.CancellationToken);

        await taking.Should().ThrowAsync<OperationCanceledException>();
        VibeHomeTurns.Held(folder).Should().BeFalse("a turn that never got its place must not keep the folder's in-process turn until a restart");
    }

    // ---------- a question row's copies are made inside the row's budget (the #811 checkpoint round) ----------

    /// <summary>
    /// Granted roots are copied one after another before Vibe starts, each up to D6's ceilings — ten slow roots would spend
    /// minutes of the row's budget before its launch. The copies are bounded by the row's own budget: past it the place is
    /// refused naming how many roots were copied of how many, and the copies made are disposed.
    /// </summary>
    [Fact]
    public async Task CopiesThatOutrunTheRowsBudget_RefuseThePlace_SayingHowManyWereCopied_AndKeepNone()
    {
        var launcher = new ProcessLauncher();
        var fast = await RepositoryAsync(launcher, "fast");
        var slow = await RepositoryAsync(launcher, "slow");
        var budget = new SnapshotBudget(long.MaxValue / 2, 0, static _ => long.MaxValue / 2);
        var places = Places();
        places.Maker = new ReviewSnapshots(launcher, SnapshotOptions.Default with
        {
            // The slow root's file takes longer to copy than the whole budget.
            BeforeCopyOf = (_, path) =>
            {
                if (path.Contains("slow", StringComparison.OrdinalIgnoreCase))
                {
                    Thread.Sleep(TimeSpan.FromSeconds(3));
                }
            },
        }, budget, ReviewSnapshots.Quiet);
        var folder = Directory.CreateDirectory(_dir.At("row")).FullName;

        using var place = await places.OneShotAsync(folder, [fast, slow], TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        place.Refusal.Should().Contain("1 of 2", "the sentence says how far the copying got").And.Contain("budget");
        Directory.EnumerateDirectories(Path.Combine(folder, "tree")).Should().BeEmpty("the copy that was made is disposed");
        budget.Held.Should().Be(0, "and its bytes are given back");
    }

    private async Task<string> RepositoryAsync(ProcessLauncher launcher, string name)
    {
        var at = Directory.CreateDirectory(_dir.At(name)).FullName;
        await File.WriteAllTextAsync(Path.Combine(at, name + ".txt"), name, TestContext.Current.CancellationToken);
        foreach (var args in (string[][])[["init", "-b", "main"], ["add", "."]])
        {
            (await launcher.RunAsync(new ProcessRequest("git", args, at), TestContext.Current.CancellationToken)).ExitCode.Should().Be(0);
        }

        return at;
    }

    // ---------- a stamp nobody can read is no owner ----------

    private string StampedFolder(string text)
    {
        var folder = Directory.CreateDirectory(_dir.At("conversation-" + Guid.NewGuid().ToString("N")[..6])).FullName;
        File.WriteAllText(Path.Combine(folder, ConversationFolder.StampName), text);

        return folder;
    }

    [Theory]
    [InlineData("store=a\nstore=b\nstarted=2026-10-13T10:00:00.0000000Z\nceilingSeconds=750\nended=true\n", "a key twice")]
    [InlineData("store=a\nstarted=2026-10-13T10:00:00.0000000Z\nceilingSeconds=1e300\nended=true\n", "a ceiling past what a TimeSpan holds")]
    [InlineData("store=a\nstarted=2026-10-13T10:00:00.0000000Z\nceilingSeconds=NaN\nended=true\n", "a ceiling that is no number")]
    [InlineData("store=a\nstarted=2026-10-13T10:00:00.0000000Z\nceilingSeconds=-5\nended=true\n", "a negative ceiling")]
    public void AMalformedStamp_IsReadAsUnowned_NeverThrown(string text, string what)
    {
        var folder = StampedFolder(text);

        var read = () => ConversationFolder.Read(folder);

        read.Should().NotThrow(what).Which.Should().Be(ConversationStamp.Unowned, $"{what}: no owner can be read from it, so the foreign-age rule applies");
    }

    [Fact]
    public void AWellFormedStamp_StillReads()
    {
        var folder = StampedFolder("store=a\nstarted=2026-10-13T10:00:00.0000000Z\nceilingSeconds=750\nended=false\n");

        ConversationFolder.Read(folder).Should().Be(new ConversationStamp("a", new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(750), Ended: false),
            "the positive companion: the malformed rows above are refused for their fault, not for the fixture's shape");
    }

    [Fact]
    public void OneFolderWithAMalformedStamp_NeverStopsThePass()
    {
        var bad = ConsultantHomes.ConversationFolder(Homes, ConsultantHomes.Vibe, ConsultationStore.NewId());
        var good = ConsultantHomes.ConversationFolder(Homes, ConsultantHomes.Vibe, ConsultationStore.NewId());
        foreach (var folder in (string[])[bad, good])
        {
            Directory.CreateDirectory(Path.Combine(folder, "tree"));
        }

        File.WriteAllText(Path.Combine(bad, ConversationFolder.StampName), "store=x\nstore=y\n");
        ConversationFolder.Begin(good, Store, DateTime.UtcNow - TimeSpan.FromDays(1), Ceiling);
        ConversationFolder.End(good);
        foreach (var folder in (string[])[bad, good])
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                Directory.SetLastWriteTimeUtc(entry, DateTime.UtcNow - TimeSpan.FromDays(1));
            }
        }

        var sweeping = () => ConsultantHomeSweep.Sweep(new HomeSweepInputs(Homes, Store, _ => null, DateTime.UtcNow, TimeSpan.FromMinutes(15)), _ => { });

        sweeping.Should().NotThrow().Which.Should().Be(1, "the stamped folder went; the malformed one is unowned and a day old");
        Directory.Exists(good).Should().BeFalse();
        Directory.Exists(bad).Should().BeTrue("unowned: left until the foreign age");
    }
}
