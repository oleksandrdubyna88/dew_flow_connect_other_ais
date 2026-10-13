using System.Text;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A persistent consultant home has a BOUNDED life (D20 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): a conversation's
/// folder under <c>coai-vibe/</c> or <c>coai-kimi/</c> is removed on the consultation sweep's beat and at server start once
/// its conversation is over, unknown to the store, or idle past the window — never while a turn could still run in it
/// (its locks, its turn's ceiling), never a folder another store made, never through a link, and never as the reason the
/// sweep fails.
/// </summary>
/// <remarks>
/// Against fake roots and real record stores in temp folders — never the machine's own local data. A Kimi home here is
/// only its SHAPE (a <c>credentials</c> junction to a folder standing in for the person's): no Kimi adapter exists yet, and
/// the sweep is runtime-neutral, so it must already be safe for the home a later build will make.
/// </remarks>
public sealed class ConsultantHomeSweepTests : IAsyncLifetime
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>A consult turn's ceiling at the default ten-minute launch: <c>ConsultationDeadline.For</c>.</summary>
    private static readonly TimeSpan Ceiling = ConsultationDeadline.For(TimeSpan.FromMinutes(10));

    private static readonly byte[] Token = Encoding.UTF8.GetBytes("""{"refresh_token":"not-a-real-one"}""");

    private readonly TempDir _dir = TempDir.For("coai-answers-homesweep-");
    private readonly List<string> _said = [];
    private readonly List<string> _deleted = [];
    private ConsultationStore _store = null!;

    private string Homes => _dir.At("local");

    private string Data => _dir.At("data");

    /// <summary>This test's store as an owner — what its folders are stamped with, unless a test says another.</summary>
    private string Store => ConversationFolder.StoreOf(Data);

    private string Persons => _dir.At("persons-credentials");

    private string Secret => Path.Combine(Persons, "kimi-code-env.json");

    public async ValueTask InitializeAsync()
    {
        _store = new ConsultationStore(Data);
        Directory.CreateDirectory(Persons);
        await File.WriteAllBytesAsync(Secret, Token, TestContext.Current.CancellationToken);
        File.SetAttributes(Secret, FileAttributes.ReadOnly);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories).Where(file => !Runners.Snapshots.SnapshotLinks.IsLink(file)))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

    private static ConsultationRecord Record(string status, DateTime? updated = null) =>
        new(ConsultationStore.NewId(), "caller-1", CallerIdentity.Claude, "no-session", "D:/repo", "main", "0123abc",
            "mistral", "glm-5-3", "vibe", "vendorRemembers", 5, ConsultationStore.Stamp(DateTime.UtcNow))
        {
            Status = status,
            RunnerPid = Environment.ProcessId,
            UpdatedUtc = ConsultationStore.Stamp(updated ?? DateTime.UtcNow),
        };

    /// <summary>
    /// A conversation's folder with a home and a snapshot in it, every stamp <paramref name="age"/> old, stamped by
    /// <paramref name="store"/> (this test's unless said) with a turn that started then and ENDED — or, with
    /// <paramref name="running"/>, a turn that started <paramref name="startedAgo"/> ago and never ended (a killed server).
    /// </summary>
    private string Home(string homes, string id, TimeSpan age, string? store = null, bool running = false, TimeSpan? startedAgo = null, string? root = null)
    {
        var folder = ConsultantHomes.ConversationFolder(root ?? Homes, homes, id);
        var home = Path.Combine(folder, "vh-0123456789");
        Directory.CreateDirectory(Path.Combine(home, "logs"));
        Directory.CreateDirectory(Path.Combine(folder, "tree"));
        File.WriteAllText(Path.Combine(home, "vibehistory"), "x");
        File.WriteAllText(Path.Combine(folder, "tree", "Parser.cs"), "x");
        if (store is not "")
        {
            ConversationFolder.Begin(folder, store ?? Store, DateTime.UtcNow - (startedAgo ?? age), Ceiling);
            if (!running)
            {
                ConversationFolder.End(folder);
            }
        }

        Aged(folder, age);

        return folder;
    }

    /// <summary>The folder and its direct entries stamped <paramref name="age"/> ago — what the sweep reads.</summary>
    private static void Aged(string folder, TimeSpan age)
    {
        var then = DateTime.UtcNow - age;
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder).Where(entry => !Runners.Snapshots.SnapshotLinks.IsLink(entry)))
        {
            Directory.SetLastWriteTimeUtc(entry, then);
        }

        Directory.SetLastWriteTimeUtc(folder, then);
    }

    private HomeSweepInputs Inputs(string? store = null, Func<string, ConsultationRecord?>? recordOf = null, string? root = null) =>
        new(root ?? Homes, store ?? Store, recordOf ?? _store.Read, DateTime.UtcNow, Window);

    /// <summary>The shared deleter, recording every entry it is handed.</summary>
    private void Recording(string entry)
    {
        _deleted.Add(entry);
        GitScratch.DeleteEvenIfReadOnly(entry);
    }

    private int Sweep(HomeSweepInputs? inputs = null, Action<string>? delete = null) =>
        ConsultantHomeSweep.Sweep(inputs ?? Inputs(), _said.Add, delete ?? Recording);

    private void TheSecretIsUntouched()
    {
        File.ReadAllBytes(Secret).Should().Equal(Token, "the person's credentials keep their bytes");
        File.GetAttributes(Secret).HasFlag(FileAttributes.ReadOnly).Should().BeTrue("and their attribute — the sweep never walked through the link");
    }

    // ---------- the rules for this store's own folders ----------

    [Fact]
    public async Task AnOrphanedKimiHome_PastTheWindow_IsGone_JunctionAndAll_AndThePersonsFolderKeepsItsFile()
    {
        var folder = Home(ConsultantHomes.Kimi, ConsultationStore.NewId(), Window * 2);
        var junction = Path.Combine(folder, "vh-0123456789", "credentials");
        await DirectoryLink.MakeAsync(new ProcessLauncher(), junction, Persons);
        File.Exists(Path.Combine(junction, "kimi-code-env.json")).Should().BeTrue("the fixture's junction must reach the person's folder");
        Aged(folder, Window * 2);

        Sweep().Should().Be(1);

        Directory.Exists(folder).Should().BeFalse("rule (a): no record, and nothing written inside the window");
        TheSecretIsUntouched();
    }

    [Fact]
    public void AnOrphanedVibeHome_PastTheWindow_IsGone_AndOneInsideTheWindowIsKept()
    {
        var old = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2);
        var young = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), TimeSpan.FromMinutes(1));

        Sweep().Should().Be(1);

        Directory.Exists(old).Should().BeFalse();
        Directory.Exists(young).Should().BeTrue("a home made a moment ago may be one whose record write is still in flight");
    }

    [Fact]
    public void ALiveConversationsHome_IsKept_AndARunningTurnsHomeEvenWhenItsStampsAreOld()
    {
        var open = Record(ConsultationStatuses.Open);
        var asking = Record(ConsultationStatuses.Asking, DateTime.UtcNow - Window * 2);
        _store.Write(open);
        _store.Write(asking);
        var openHome = Home(ConsultantHomes.Vibe, open.Id, TimeSpan.FromMinutes(1));
        var askingHome = Home(ConsultantHomes.Vibe, asking.Id, Window * 2, running: true, startedAgo: TimeSpan.FromMinutes(2));

        Sweep().Should().Be(0);

        Directory.Exists(openHome).Should().BeTrue("an open conversation written inside the window is alive");
        Directory.Exists(askingHome).Should().BeTrue("its turn started two minutes ago — inside its ceiling, so its child may still run");
    }

    [Theory]
    [InlineData(ConsultationStatuses.Closed)]
    [InlineData(ConsultationStatuses.Failed)]
    public void AnOverConversationsHome_IsGoneOnTheBeat_HoweverYoung(string status)
    {
        var record = Record(status);
        _store.Write(record);
        var folder = Home(ConsultantHomes.Vibe, record.Id, TimeSpan.Zero);

        Sweep().Should().Be(1);

        Directory.Exists(folder).Should().BeFalse("rule (b): its conversation is over");
    }

    [Fact]
    public void AnOpenConversationIdlePastTheWindow_LosesItsHome()
    {
        var record = Record(ConsultationStatuses.Open);
        _store.Write(record);
        var folder = Home(ConsultantHomes.Vibe, record.Id, Window * 2);

        Sweep().Should().Be(1);

        Directory.Exists(folder).Should().BeFalse("rule (c): the record pass lapses it too, so the home goes now");
    }

    /// <summary>
    /// P0 of the plan round: a host that died mid-turn leaves an <c>asking</c> record and a stamp that never ended. Once the
    /// turn's own ceiling has passed and nobody holds its lock, that home is due like an over one; inside the ceiling it is
    /// kept, whatever its stamps say.
    /// </summary>
    [Fact]
    public void AnAskingRecordWhoseHostDied_IsDueOnceItsTurnsCeilingHasPassed_NeverBefore()
    {
        var stale = Record(ConsultationStatuses.Asking, DateTime.UtcNow - Ceiling * 2);
        var running = Record(ConsultationStatuses.Asking, DateTime.UtcNow - Ceiling * 2);
        _store.Write(stale);
        _store.Write(running);
        var staleHome = Home(ConsultantHomes.Vibe, stale.Id, Ceiling * 2, running: true, startedAgo: Ceiling + TimeSpan.FromMinutes(1));
        var runningHome = Home(ConsultantHomes.Vibe, running.Id, Ceiling * 2, running: true, startedAgo: Ceiling - TimeSpan.FromMinutes(1));

        Sweep().Should().Be(1);

        Directory.Exists(staleHome).Should().BeFalse("its turn's ceiling has passed and no lock is held: nothing of it still runs");
        Directory.Exists(runningHome).Should().BeTrue("a minute inside its ceiling its Vibe child may still be running");
    }

    /// <summary>
    /// R4: a server killed with a Vibe child alive. The child is bounded by its own <c>--time-limit</c>, which is ten seconds
    /// under the launch's timeout (<c>VibeRuntime.TimeLimit</c>), and the turn's ceiling — what the sweep waits out — is
    /// that timeout plus its grace (<c>ConsultationDeadline.For</c>). The ceiling must exceed the child's life for every
    /// timeout a row can carry; the window alone is never what protects it.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(240)]
    public void TheTurnsCeiling_OutlivesTheVibeChildsOwnTimeLimit(int timeoutMinutes)
    {
        var timeout = TimeSpan.FromMinutes(timeoutMinutes);

        TimeSpan.FromSeconds(VibeRuntime.TimeLimit(timeout)).Should().BeLessThan(timeout, "Vibe stops itself under the launch's timeout (D8)");
        ConsultationDeadline.For(timeout).Should().BeGreaterThan(timeout, "the ceiling the sweep waits out is the timeout plus its grace");
    }

    [Fact]
    public void AnOverConversationWhoseTurnNeverEnded_IsKeptUntilItsCeilingPasses()
    {
        // R4 the other way round: the record pass closed it (row timeout past the idle window), but its child may still run.
        var record = Record(ConsultationStatuses.Closed);
        _store.Write(record);
        var folder = Home(ConsultantHomes.Vibe, record.Id, Window * 2, running: true, startedAgo: TimeSpan.FromMinutes(5));

        Sweep().Should().Be(0);

        Directory.Exists(folder).Should().BeTrue("its turn never ended and its ceiling has not passed");
    }

    // ---------- whose folder it is, and the locks (R1) ----------

    /// <summary>
    /// R1 (a): two servers with two data directories share one homes root. A folder the OTHER store made is not judged by
    /// this store's records — its own records may well be open — and is left alone until it has not been written for the
    /// foreign age (seven days); one nobody stamped the same.
    /// </summary>
    [Fact]
    public void AnotherStoresFolder_AndAnUnstampedOne_AreLeftAlone_UntilTheyArePastTheForeignAge()
    {
        var other = ConversationFolder.StoreOf(_dir.At("other-data"));
        var theirs = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), TimeSpan.FromDays(1), store: other);
        var unstamped = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), TimeSpan.FromDays(1), store: "");
        var abandoned = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), TimeSpan.FromDays(8), store: other);

        Sweep().Should().Be(1);

        Directory.Exists(theirs).Should().BeTrue("a day old, idle past this store's window — and none of this store's business");
        Directory.Exists(unstamped).Should().BeTrue("no stamp, no owner: judged by age alone");
        Directory.Exists(abandoned).Should().BeFalse("past the record store's own retention nobody's record can still be open");
        _deleted.Should().NotContain(entry => entry.StartsWith(theirs, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>R1 (b): a turn held in ANOTHER process — the folder's turn lock, which the in-process turns cannot see.</summary>
    [Fact]
    public void AFolderWhoseTurnLockIsHeldElsewhere_IsSkipped_NothingDeleted_AndGoneOnceLetGo()
    {
        var record = Record(ConsultationStatuses.Closed);
        _store.Write(record);
        var folder = Home(ConsultantHomes.Vibe, record.Id, Window * 2);
        var elsewhere = ConversationFolder.TakeTurn(folder).Should().BeOfType<Hold.Taken>().Which.Stream;

        Sweep().Should().Be(0, "another process runs a turn in it");
        _deleted.Should().BeEmpty("not one entry is touched while the lock is held");
        Directory.Exists(folder).Should().BeTrue();

        elsewhere.Dispose();
        Sweep().Should().Be(1);
        Directory.Exists(folder).Should().BeFalse();
    }

    [Fact]
    public void TheRulesAreAskedAgainUnderTheLocks_SoAConversationThatCameBackMeanwhileIsKept()
    {
        var record = Record(ConsultationStatuses.Closed);
        var folder = Home(ConsultantHomes.Vibe, record.Id, TimeSpan.Zero);
        var asked = 0;
        // Over when first asked; open and just written when asked again under the locks.
        ConsultationRecord? Read(string id) => ++asked == 1 ? record : record with { Status = ConsultationStatuses.Open };

        Sweep(Inputs(recordOf: Read)).Should().Be(0);

        asked.Should().Be(2, "once before the locks, once under them");
        _deleted.Should().BeEmpty();
        Directory.Exists(folder).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AHomeWhoseInProcessTurnIsHeld_IsSkipped_AndGoneOnTheNextBeatOnceGivenBack(bool theHomeNotTheFolder)
    {
        var folder = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2);
        var held = await VibeHomeTurns.TakeAsync(theHomeNotTheFolder ? Path.Combine(folder, "vh-0123456789") : folder, TestContext.Current.CancellationToken);

        Sweep().Should().Be(0, "a live launch is judged by the lock, never by a stamp");
        Directory.Exists(folder).Should().BeTrue();

        held.Dispose();
        Sweep().Should().Be(1);
        Directory.Exists(folder).Should().BeFalse();
    }

    // ---------- never through a link (R2) ----------

    /// <summary>
    /// R2: the homes folder itself redirected — <c>coai-vibe</c> a junction to a folder outside, holding an aged conversation
    /// and a read-only sentinel. Nothing is swept through it: the conversation stays, the sentinel keeps its bytes and its
    /// attribute, and the sweep says why.
    /// </summary>
    [Fact]
    public async Task AHomesFolderThatIsALink_IsNeverSweptThrough()
    {
        var outside = _dir.At("outside");
        var conversation = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2, root: outside);
        var sentinel = Path.Combine(outside, ConsultantHomes.Vibe, "sentinel.txt");
        await File.WriteAllBytesAsync(sentinel, Token, TestContext.Current.CancellationToken);
        File.SetAttributes(sentinel, FileAttributes.ReadOnly);
        Directory.CreateDirectory(Homes);
        await DirectoryLink.MakeAsync(new ProcessLauncher(), Path.Combine(Homes, ConsultantHomes.Vibe), Path.Combine(outside, ConsultantHomes.Vibe));

        Sweep().Should().Be(0);

        Directory.Exists(conversation).Should().BeTrue("the aged conversation behind the link is not this root's");
        File.ReadAllBytes(sentinel).Should().Equal(Token);
        File.GetAttributes(sentinel).HasFlag(FileAttributes.ReadOnly).Should().BeTrue();
        _said.Should().Contain(line => line.Contains("is a link", StringComparison.Ordinal));
        DirectoryLink.Remove(Path.Combine(Homes, ConsultantHomes.Vibe));
    }

    [Fact]
    public async Task AConversationFolderThatIsALink_IsLeftAlone_AndItsTargetUntouched()
    {
        var outside = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2, root: _dir.At("outside"));
        var link = ConsultantHomes.ConversationFolder(Homes, ConsultantHomes.Vibe, ConsultationStore.NewId());
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        await DirectoryLink.MakeAsync(new ProcessLauncher(), link, outside);

        Sweep().Should().Be(0);

        Directory.Exists(outside).Should().BeTrue();
        File.Exists(Path.Combine(outside, "tree", "Parser.cs")).Should().BeTrue("nothing behind the link was deleted");
        _said.Should().Contain(line => line.Contains("is a link", StringComparison.Ordinal));
        DirectoryLink.Remove(link);
    }

    // ---------- never the reason a sweep fails ----------

    [Fact]
    public void AFolderThatCannotBeRemoved_IsSaid_AndTheRestOfThePassRuns()
    {
        var stuck = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2);
        var other = Home(ConsultantHomes.Kimi, ConsultationStore.NewId(), Window * 2);

        var removed = Sweep(delete: entry =>
        {
            if (entry.StartsWith(stuck, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("the test holds this one");
            }

            GitScratch.DeleteEvenIfReadOnly(entry);
        });

        removed.Should().Be(1);
        Directory.Exists(other).Should().BeFalse("the pass goes on past the folder it could not remove");
        _said.Should().ContainSingle(line => line.Contains(stuck, StringComparison.Ordinal) && line.Contains("next beat", StringComparison.Ordinal));
    }

    [Fact]
    public void NoHomesRoot_SweepsNothing()
    {
        Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2);

        Sweep(Inputs(root: string.Empty)).Should().Be(0,
            "settings built by hand name no homes root, so a test never sweeps the machine's own local data");
    }

    // ---------- through the service: the start sweep, and the beat's order ----------

    private PanelService Service() => new(
        new PanelSettings
        {
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = Data,
            ConsultIdle = Window,
            ConsultantHomesRoot = Homes,
        },
        VaultKeys.None("no vault in tests"),
        default,
        new ProcessLauncher(),
        Logger.None, Noticing.None);

    [Fact]
    public void AServiceBuiltWithAStaleHomePresent_RemovesItAtStart()
    {
        var folder = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2);

        _ = Service();

        Directory.Exists(folder).Should().BeFalse("a host killed mid-conversation leaves its homes until the next start");
    }

    [Fact]
    public void OnOneBeat_TheRecordsAreSweptFirst_SoALapsedConversationsHomeGoesWithIt()
    {
        var service = Service();
        var record = Record(ConsultationStatuses.Open, DateTime.UtcNow - Window * 2);
        _store.Write(record);
        var folder = Home(ConsultantHomes.Vibe, record.Id, Window * 2);
        // The home's own stamps young, so only rule (b) — the record the pass just lapsed — can take it.
        Aged(folder, TimeSpan.Zero);

        service.SweepConsultations();

        _store.Read(record.Id)!.IsOver.Should().BeTrue("the record pass lapsed it");
        Directory.Exists(folder).Should().BeFalse("and its home went on the same beat, by rule (b)");
    }
}
