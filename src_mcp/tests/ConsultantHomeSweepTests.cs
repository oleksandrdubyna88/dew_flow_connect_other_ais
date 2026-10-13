using System.Text;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
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
/// its conversation is over, unknown to the store, or idle past the window — never while a turn holds it, never through a
/// link, and never as the reason the sweep fails.
/// </summary>
/// <remarks>
/// Against fake roots and a real record store in temp folders — never the machine's own local data. A Kimi home here is
/// only its SHAPE (a <c>credentials</c> junction to a folder standing in for the person's): no Kimi adapter exists yet, and
/// the sweep is runtime-neutral, so it must already be safe for the home a later build will make.
/// </remarks>
public sealed class ConsultantHomeSweepTests : IAsyncLifetime
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static readonly byte[] Token = Encoding.UTF8.GetBytes("""{"refresh_token":"not-a-real-one"}""");

    private readonly TempDir _dir = TempDir.For("coai-answers-homesweep-");
    private readonly List<string> _said = [];
    private ConsultationStore _store = null!;

    private string Homes => _dir.At("local");

    private string Persons => _dir.At("persons-credentials");

    private string Secret => Path.Combine(Persons, "kimi-code-env.json");

    public async ValueTask InitializeAsync()
    {
        _store = new ConsultationStore(_dir.At("data"));
        Directory.CreateDirectory(Persons);
        await File.WriteAllBytesAsync(Secret, Token, TestContext.Current.CancellationToken);
        File.SetAttributes(Secret, FileAttributes.ReadOnly);
    }

    public ValueTask DisposeAsync()
    {
        if (File.Exists(Secret))
        {
            File.SetAttributes(Secret, FileAttributes.Normal);
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

    /// <summary>A conversation's folder with a home and a snapshot in it, every stamp <paramref name="age"/> old.</summary>
    private string Home(string homes, string id, TimeSpan age)
    {
        var folder = ConsultantHomes.ConversationFolder(Homes, homes, id);
        var home = Path.Combine(folder, "vh-0123456789");
        Directory.CreateDirectory(Path.Combine(home, "logs"));
        Directory.CreateDirectory(Path.Combine(folder, "tree"));
        File.WriteAllText(Path.Combine(home, "vibehistory"), "x");
        File.WriteAllText(Path.Combine(folder, "tree", "Parser.cs"), "x");
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

    private int Sweep(Action<string>? delete = null) =>
        ConsultantHomeSweep.Sweep(Homes, _store.Read, DateTime.UtcNow, Window, _said.Add, delete);

    private void TheCredentialsAreUntouched()
    {
        File.ReadAllBytes(Secret).Should().Equal(Token, "the person's credentials keep their bytes");
        File.GetAttributes(Secret).HasFlag(FileAttributes.ReadOnly).Should().BeTrue("and their attribute — the sweep never walked through the junction");
    }

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
        TheCredentialsAreUntouched();
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
    public void ALiveConversationsHome_IsKept_AndAnAskingOnesEvenWhenItsStampsAreOld()
    {
        var open = Record(ConsultationStatuses.Open);
        var asking = Record(ConsultationStatuses.Asking, DateTime.UtcNow - Window * 2);
        _store.Write(open);
        _store.Write(asking);
        var openHome = Home(ConsultantHomes.Vibe, open.Id, TimeSpan.FromMinutes(1));
        var askingHome = Home(ConsultantHomes.Vibe, asking.Id, Window * 2);

        Sweep().Should().Be(0);

        Directory.Exists(openHome).Should().BeTrue("an open conversation written inside the window is alive");
        Directory.Exists(askingHome).Should().BeTrue("a turn in flight — the record pass settles it first");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AHomeWhoseTurnIsHeld_IsSkipped_AndGoneOnTheNextBeatOnceGivenBack(bool theHomeNotTheFolder)
    {
        var folder = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2);
        var held = await VibeHomeTurns.TakeAsync(theHomeNotTheFolder ? Path.Combine(folder, "vh-0123456789") : folder, TestContext.Current.CancellationToken);

        Sweep().Should().Be(0, "a live launch is judged by the lock, never by a stamp");
        Directory.Exists(folder).Should().BeTrue();

        held.Dispose();
        Sweep().Should().Be(1);
        Directory.Exists(folder).Should().BeFalse();
    }

    [Fact]
    public void AFolderThatCannotBeRemoved_IsSaid_AndTheRestOfThePassRuns()
    {
        var stuck = Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2);
        var other = Home(ConsultantHomes.Kimi, ConsultationStore.NewId(), Window * 2);

        var removed = Sweep(folder =>
        {
            if (folder == stuck)
            {
                throw new IOException("the test holds this one");
            }

            Runners.Files.GitScratch.DeleteEvenIfReadOnly(folder);
        });

        removed.Should().Be(1);
        Directory.Exists(other).Should().BeFalse("the pass goes on past the folder it could not remove");
        _said.Should().ContainSingle(line => line.Contains(stuck, StringComparison.Ordinal) && line.Contains("next beat", StringComparison.Ordinal));
    }

    [Fact]
    public void NoHomesRoot_SweepsNothing()
    {
        Home(ConsultantHomes.Vibe, ConsultationStore.NewId(), Window * 2);

        ConsultantHomeSweep.Sweep(string.Empty, _store.Read, DateTime.UtcNow, Window, _said.Add).Should().Be(0,
            "settings built by hand name no homes root, so a test never sweeps the machine's own local data");
    }

    // ---------- through the service: the start sweep, and the beat's order ----------

    private PanelService Service() => new(
        new PanelSettings
        {
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _dir.At("data"),
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
