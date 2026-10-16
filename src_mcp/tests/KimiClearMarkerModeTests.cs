using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>coai-mcp --kimi-clear-marker &lt;path&gt;</c> (DL9, S-K18; A3 §7): the card's button deletes ONE survivor marker, only when every process it names is gone, and signals nothing. Exit codes are
/// fixed — 0 cleared or absent, 65 a path that is not a marker under the lock root, 75 a member still runs, 74 a member or the file cannot be checked — and never 64.
/// </summary>
public sealed class KimiClearMarkerModeTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private readonly TempDir _dir = TempDir.For("testsToDelete-clear-marker-");
    private readonly List<string> _notes = [];
    private readonly StringWriter _stdout = new();

    private string Root => _dir.At("locks");

    private string MarkerPath => Path.Combine(Root, "0123456789abcdef.survivors.json");

    public void Dispose() => _dir.Dispose();

    private static ProcessIdentity Member(int pid) => new(pid, Started);

    private static KimiSurvivorMarkerDto Running(params ProcessIdentity[] tree) =>
        new(new ReaderMarkerDto(ReaderPhases.Running, 424242, Started, DateTimeOffset.UtcNow, tree), "/owned/kimi-root", "/people/sign-in");

    private static KimiSurvivorMarkerDto Spawning() =>
        new(new ReaderMarkerDto(ReaderPhases.Spawning, 424242, Started, DateTimeOffset.UtcNow), "/owned/kimi-root", "/people/sign-in");

    private Task<int> RunAsync(string[] args, Func<ProcessIdentity, Liveness>? liveness = null, Func<KimiPresence>? presence = null) =>
        KimiClearMarkerMode.RunAsync(args, _notes.Add, _stdout, Root, liveness ?? (_ => new Liveness.Gone("no such process")), presence ?? (() => new KimiPresence.NoKimi()));

    private Task<int> ClearAsync(Func<ProcessIdentity, Liveness>? liveness = null, Func<KimiPresence>? presence = null) =>
        RunAsync(["--kimi-clear-marker", MarkerPath], liveness, presence);

    private string Notes => string.Join("\n", _notes);

    private JsonElement Report() => JsonDocument.Parse(_stdout.ToString().Trim()).RootElement.Clone();

    [Fact]
    public void KimiClearMarker_IsAMode_NotAnUnknownArgument() =>
        Program.Classify(["--kimi-clear-marker"]).Should().NotBe(Program.Startup.Usage, "an unknown args[0] exits 64 — a known mode never answers 64");

    // ---------- 0 ----------

    [Fact]
    public async Task EveryMemberGoneOrReused_ClearsTheMarker_Exits0()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243), Member(424244)));

        var exit = await ClearAsync(member => member.Pid == 424243 ? new Liveness.Gone("no such process") : new Liveness.Gone("the pid is reused — started at 2026-10-16T09:00:00.0000000Z"));

        exit.Should().Be(0, Notes);
        File.Exists(MarkerPath).Should().BeFalse("the marker outlived members that are all gone");
        Notes.Should().Contain("cleared").And.Contain("424243").And.Contain("424244");
        Report().GetProperty("outcome").GetString().Should().Be("cleared");
        Report().GetProperty("members").GetArrayLength().Should().Be(2);
        Report().GetProperty("members")[0].GetProperty("liveness").GetString().Should().Be("gone");
    }

    [Fact]
    public async Task NoMarkerAtThePath_Exits0_SayingNothingToClear()
    {
        var exit = await ClearAsync();

        exit.Should().Be(0);
        Notes.Should().Contain("no marker").And.Contain("nothing to clear");
        Report().GetProperty("outcome").GetString().Should().Be("absent");
    }

    // ---------- 75: a living member ----------

    [Fact]
    public async Task ALivingMember_KeepsTheMarker_Exits75_NamingThePid()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));

        var exit = await ClearAsync(_ => new Liveness.Alive());

        exit.Should().Be(75, "the clear mode removed a marker over a living survivor (pid 424243)");
        File.Exists(MarkerPath).Should().BeTrue();
        Notes.Should().Contain("424243").And.Contain("still running").And.Contain("never ends a process");
        Report().GetProperty("members")[0].GetProperty("liveness").GetString().Should().Be("alive");
    }

    [Fact]
    public async Task AliveBeatsUnknown_ALivingMemberAndAnUnreadableOne_Exit75_Not74()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243), Member(424244)));

        var exit = await ClearAsync(member => member.Pid == 424243 ? new Liveness.Unknown("EACCES") : new Liveness.Alive());

        exit.Should().Be(75, "a living survivor is the stronger fact");
        File.Exists(MarkerPath).Should().BeTrue();
    }

    // ---------- 74: cannot be checked ----------

    [Theory]
    [InlineData("errno 13 (EACCES) reading /proc/424243/stat")]
    [InlineData("errno 5 (EIO) reading /proc/424243/stat")]
    [InlineData("not a /proc stat line: garbage")]
    public async Task AMemberThatCannotBeChecked_KeepsTheMarker_Exits74_NamingTheError(string reason)
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));

        var exit = await ClearAsync(_ => new Liveness.Unknown(reason));

        exit.Should().Be(74, "a member whose /proc cannot be read was taken as gone, and the marker was cleared");
        File.Exists(MarkerPath).Should().BeTrue();
        Notes.Should().Contain("424243").And.Contain(reason).And.Contain("not known to be gone");
    }

    [Fact]
    public async Task AMarkerThatCannotBeParsed_IsKept_AndExits74_NotAnArgumentFault()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(MarkerPath, "{ nope");

        var exit = await ClearAsync();

        exit.Should().Be(74);
        File.Exists(MarkerPath).Should().BeTrue();
        Notes.Should().Contain("could not be read").And.Contain("not known to be safe to delete");
    }

    // ---------- 65: a request fault; nothing is deleted ----------

    [Fact]
    public async Task NoPath_OrAPathOutsideTheLockRoot_Exits65_DeletesNothing()
    {
        Directory.CreateDirectory(Root);
        var decoy = _dir.At("0123456789abcdef.survivors.json");
        KimiSurvivorMarker.Write(decoy, Running(Member(424243)));
        var elsewhere = Path.Combine(_dir.At("other-locks"), "0123456789abcdef.survivors.json");
        KimiSurvivorMarker.Write(elsewhere, Running(Member(424243)));
        var notAMarkerName = Path.Combine(Root, "notes.txt");
        File.WriteAllText(notAMarkerName, "keep me");
        var inASubfolder = Path.Combine(Root, "sub", "0123456789abcdef.survivors.json");
        KimiSurvivorMarker.Write(inASubfolder, Running(Member(424243)));

        var exits = new[]
        {
            await RunAsync(["--kimi-clear-marker"]),
            await RunAsync(["--kimi-clear-marker", "--something"]),
            await RunAsync(["--kimi-clear-marker", decoy]),
            await RunAsync(["--kimi-clear-marker", elsewhere]),
            await RunAsync(["--kimi-clear-marker", notAMarkerName]),
            await RunAsync(["--kimi-clear-marker", inASubfolder]),
            await RunAsync(["--kimi-clear-marker", Path.Combine(Root, "..", "0123456789abcdef.survivors.json")]),
        };

        exits.Should().AllBeEquivalentTo(65, "exit was 64 — a known mode never answers 64");
        new[] { decoy, elsewhere, notAMarkerName, inASubfolder }.Should().OnlyContain(path => File.Exists(path), "the clear mode deleted a file outside the Kimi lock root");
        Notes.Should().Contain("needs <path>").And.Contain("is not a Kimi survivor marker under");
    }

    // ---------- a marker that names nothing ----------

    [Fact]
    public async Task ASpawningMarker_IsClearedOnlyWhenNoKimiRuns()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning());
        (await ClearAsync(presence: () => new KimiPresence.Running([4711]))).Should().Be(75, "a spawning marker was cleared while a kimi runs");
        Notes.Should().Contain("4711");
        File.Exists(MarkerPath).Should().BeTrue();

        (await ClearAsync(presence: () => new KimiPresence.Unreadable("EACCES"))).Should().Be(74);
        File.Exists(MarkerPath).Should().BeTrue();

        (await ClearAsync(presence: () => new KimiPresence.NoKimi())).Should().Be(0);
        File.Exists(MarkerPath).Should().BeFalse();
    }
}
