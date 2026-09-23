using System.Collections.Concurrent;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A run says when it started, keeps a heartbeat, and the next start records one that never finished.
/// </summary>
/// <remarks>
/// <para><b>What was broken.</b> A server killed by SIGKILL, OOM, a power cut or a background-thread
/// crash ran no <c>catch</c> and wrote nothing, so the page showed a server that simply stopped. The
/// code <c>unclean-exit</c> was reserved and nothing could write it: nobody is left to say a process
/// died except the next one to start, and only if the dead one left something behind. (Story 3.1.)</para>
/// <para>The planner is tested apart from the disk because the danger is in the DECISION — recording a
/// live run as dead — and the I/O is tested against a real directory with the clock and the append
/// injected, because what matters there is the ORDER: claim, record, and only then remove.</para>
/// <para><b>The race test earned its place before anything shipped.</b> The first design claimed a
/// death by RENAMING its marker, and two sweepers racing over fifty stale markers recorded 72 deaths.
/// Measured in isolation, .NET's <c>File.Move</c> let both of two concurrent renames of one file
/// succeed 975 times in 1000; <c>FileMode.CreateNew</c> let both succeed 0 times. The claim is an
/// exclusive create now.</para>
/// </remarks>
public sealed class TheRunsAreAccountedForTests : IDisposable
{
    /// <summary>
    /// The REAL time, captured once — not a fixed date.
    /// </summary>
    /// <remarks>
    /// A claim a sweep creates takes its file time from the real clock, and a claim's age is judged
    /// against the sweep's clock. With a fixed noon here, a test run at 08:00 would see a sibling's
    /// claim created a millisecond ago as four hours old — abandoned — and break it: manufacturing the
    /// very duplicate the race test exists to catch. Production reads one clock for both, and so does
    /// this.
    /// </remarks>
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly TempDir _dir = TempDir.For("coai-runs-");

    private readonly ConcurrentBag<ServerNotice> _recorded = [];

    public void Dispose() => _dir.Dispose();

    private static RunMarker Marker(string run, string host = "other-machine", int pid = 4242, TimeSpan? silentFor = null) =>
        new(run, pid, host, Now.AddHours(-2), Now - (silentFor ?? TimeSpan.Zero));

    private static readonly RunMarker Me = Marker("me0000000000", host: "this-machine", pid: 1);

    private static FoundMarker Plain(RunMarker marker, TimeSpan? age = null) =>
        new($"{marker.Run}.json", MarkerFileKind.Marker, marker.Run, marker, Now - (age ?? TimeSpan.Zero));

    private static FoundMarker Claim(string run, TimeSpan age) =>
        new($"{run}.claim", MarkerFileKind.Claim, run, null, Now - age);

    private static MarkerSweep Planned(params FoundMarker[] found) =>
        RunMarkers.Plan(found, Now, Me, RunMarkers.Stale, (_, _) => false);

    private static readonly TimeSpan AnHour = TimeSpan.FromHours(1);

    // ---------------------------------------------------------------- the decision, apart from the disk

    [Fact]
    public void AFreshMarker_IsALiveRun_AndIsNotRecorded()
    {
        Planned(Plain(Marker("fresh0000000", silentFor: TimeSpan.FromMinutes(5)))).Claim
            .Should().BeEmpty("a run that beat five minutes ago is alive, wherever it runs");
    }

    [Fact]
    public void AStaleMarker_FromAnotherMachine_IsADeath()
    {
        Planned(Plain(Marker("dead00000000", silentFor: AnHour))).Claim
            .Should().ContainSingle().Which.Run.Should().Be("dead00000000");
    }

    [Fact]
    public void ExactlyAtTheWindow_IsStillFresh()
    {
        Planned(Plain(Marker("edge00000000", silentFor: RunMarkers.Stale))).Claim
            .Should().BeEmpty("the window is inclusive: thirty minutes silent is the last minute it is believed");
    }

    [Fact]
    public void ThisRunsOwnMarker_IsNeverRecorded_HoweverOld()
    {
        Planned(Plain(Me with { HeartbeatUtc = Now.AddDays(-3) })).Claim
            .Should().BeEmpty("a run does not record its own death while it runs");
    }

    [Fact]
    public void AStaleMarkerFromThisMachine_WhoseProcessIsStillAlive_IsNotADeath()
    {
        // The plan round's refinement (gemini): a pid means nothing across a share, but on THIS
        // machine, paired with the process start time, it does. A server paused under a debugger or
        // stalled for half an hour is not dead, and recording it would be a death that did not happen.
        var paused = Marker("paused000000", host: "THIS-MACHINE", silentFor: AnHour);

        var decided = RunMarkers.Plan([Plain(paused)], Now, Me, RunMarkers.Stale,
            (pid, started) => pid == paused.Pid && started == paused.StartedUtc);

        decided.Claim.Should().BeEmpty("its process is alive and is provably the one that wrote the marker");
    }

    [Fact]
    public void AStaleMarkerFromThisMachine_WhoseProcessIsGone_IsADeath()
    {
        Planned(Plain(Marker("gone00000000", host: "this-machine", silentFor: AnHour))).Claim
            .Should().ContainSingle();
    }

    [Fact]
    public void AClaimStillBeingWorkedOn_BlocksRecordingTheSameDeath()
    {
        // A live sweeper holds a claim for milliseconds; a YOUNG claim is one being recorded right now.
        var decided = Planned(Plain(Marker("held00000000", silentFor: AnHour)), Claim("held00000000", TimeSpan.FromSeconds(2)));

        decided.Claim.Should().BeEmpty("another start is recording this death at this moment");
        decided.Break.Should().BeEmpty();
    }

    [Fact]
    public void AnAbandonedClaim_WithItsMarkerStillThere_IsBroken_AndTheDeathRecorded()
    {
        // A sweeper that died between claiming and recording: the claim is old, and the marker that
        // says the run died is still there, so the death is unrecorded.
        var decided = Planned(Plain(Marker("lost00000000", silentFor: AnHour * 3)), Claim("lost00000000", AnHour));

        decided.Break.Should().ContainSingle().Which.Run.Should().Be("lost00000000");
        decided.Claim.Should().ContainSingle().Which.Run.Should().Be("lost00000000");
    }

    [Fact]
    public void AClaimWhoseMarkerIsGone_IsALeftover_AndIsRetired()
    {
        // The death was recorded and the marker removed; the sweeper died before removing its claim.
        var decided = Planned(Claim("done00000000", AnHour));

        decided.Retire.Should().ContainSingle().Which.Run.Should().Be("done00000000");
        decided.Claim.Should().BeEmpty("there is no marker, so there is no death left to record");
    }

    [Fact]
    public void AnUnreadableMarker_IsRetiredOnlyOnceItIsOld()
    {
        // "Could not parse it" must not become "delete a live run's marker": a file being written can
        // be read half-written. Only an OLD unreadable file is garbage.
        var young = new FoundMarker("young0000000.json", MarkerFileKind.Marker, "young0000000", null, Now.AddSeconds(-1));
        var old = new FoundMarker("rotten000000.json", MarkerFileKind.Marker, "rotten000000", null, Now - AnHour);

        var decided = Planned(young, old);

        decided.Retire.Select(one => one.Run).Should().Equal(["rotten000000"]);
        decided.Claim.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- the disk, in order

    private RunMarkers Markers(RunMarker me) =>
        new(ResolvedDataDir.For(_dir.Path), me, () => Now, (_, _) => false, Serilog.Core.Logger.None);

    private bool Landing(ServerNotice notice)
    {
        _recorded.Add(notice);

        return true;
    }

    private string Runs => Path.Combine(_dir.Path, RunMarkers.Folder);

    private string[] Left() => [.. Directory.GetFiles(Runs).Select(Path.GetFileName).OrderBy(n => n).Select(n => n!)];

    private void Leave(RunMarker marker)
    {
        Directory.CreateDirectory(Runs);
        File.WriteAllText(Path.Combine(Runs, marker.Run + ".json"),
            JsonSerializer.Serialize(marker, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public void ABeat_WritesTheDocumentedFields_InTheDocumentedNames()
    {
        Markers(Me).Write().Should().BeTrue();

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Runs, Me.Run + ".json")));
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["run", "pid", "host", "startedUtc", "heartbeatUtc"],
            "the plan's own field list, which the scenario test and any reader write against");
        json.RootElement.GetProperty("heartbeatUtc").GetDateTime().Should().Be(Now);
    }

    [Fact]
    public void ABeat_RecreatesAMarkerAnotherServerSwept()
    {
        // A beat is evidence and a sweep is a guess: a machine that slept past the window has its
        // marker swept by a peer, and its next beat must put it back or its own clean end can never
        // clear it. (The lesson collect_runs learned.)
        var markers = Markers(Me);
        markers.Write();
        File.Delete(Path.Combine(Runs, Me.Run + ".json"));

        markers.Write().Should().BeTrue();

        Left().Should().Equal([Me.Run + ".json"]);
    }

    [Fact]
    public void ASweep_RecordsEachStaleRunOnce_AndRemovesItOnlyAfterTheRecordLanded()
    {
        Leave(Marker("dead00000001", silentFor: AnHour));
        Leave(Marker("dead00000002", silentFor: AnHour * 3));
        Leave(Marker("alive0000000", silentFor: TimeSpan.FromMinutes(2)));

        Markers(Me).Sweep(Landing, CancellationToken.None).Should().Be(2);

        _recorded.Select(n => n.Subject).Should().BeEquivalentTo(["dead00000001", "dead00000002"]);
        Left().Should().Equal(["alive0000000.json"], "the recorded deaths and their claims are gone; the live run is untouched");
    }

    [Fact]
    public async Task AStopDuringTheSweep_IsHonouredBetweenDeaths_AndLeavesTheRestForTheNextStart()
    {
        // A client that connects and leaves while a start is still recording deaths on a slow share
        // used to wait out the whole stop budget, and then be told a HEARTBEAT was stuck. (gemini, the
        // code round.) The death being written finishes — a synchronous append cannot be recalled —
        // and every other one is still on disk for the next start, which is where it belongs.
        Leave(Marker("dead0000000a", silentFor: AnHour));
        Leave(Marker("dead0000000b", silentFor: AnHour));
        Leave(Marker("dead0000000c", silentFor: AnHour));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var life = RunLife.Start(Markers(Me), notice =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));

            return Landing(notice);
        }, Serilog.Core.Logger.None);
        entered.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("the sweep reached its first death");

        var stopping = life.StopAsync();
        release.Set();

        (await stopping).Should().BeTrue();
        _recorded.Should().ContainSingle("the death already being written finishes; the stop is honoured before the next");
        Left().Count(name => name.StartsWith("dead", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .Should().Be(2, "the deaths not yet recorded stay for the next start to record");
    }

    [Fact]
    public void ARepeatedStart_RecordsNothingMore()
    {
        Leave(Marker("dead00000003", silentFor: AnHour));
        Markers(Me).Sweep(Landing, CancellationToken.None);

        Markers(Me with { Run = "second000000" }).Sweep(Landing, CancellationToken.None).Should().Be(0);

        _recorded.Should().ContainSingle("exactly once across a repeated start");
    }

    [Fact]
    public void AnAppendThatDoesNotLand_LeavesTheDeathForTheNextStart()
    {
        // Removing the marker before the record is known to be on disk is how a death disappears. A
        // refused append releases the claim and keeps the marker, so a later start retries.
        Leave(Marker("dead00000004", silentFor: AnHour));

        Markers(Me).Sweep(_ => false, CancellationToken.None).Should().Be(0);
        Left().Should().Equal(["dead00000004.json"], "the marker stays and the claim is released");

        Markers(Me with { Run = "second000000" }).Sweep(Landing, CancellationToken.None).Should().Be(1);
        _recorded.Should().ContainSingle();
    }

    [Fact]
    public async Task TwoStartsRacingOverOneShare_RecordEachDeathOnce()
    {
        // Two MCP clients starting together over one data directory is ordinary — an editor reloading
        // with two windows — and both find the same stale markers. This is the test that found the
        // rename-based claim recording 72 deaths for 50.
        const int Deaths = 50;
        for (var i = 0; i < Deaths; i += 1)
        {
            Leave(Marker($"race{i:D8}", silentFor: AnHour));
        }

        var first = Markers(Me);
        var second = Markers(Me with { Run = "other0000000" });
        first.Write();
        second.Write();
        await Task.WhenAll(Task.Run(() => first.Sweep(Landing, CancellationToken.None)), Task.Run(() => second.Sweep(Landing, CancellationToken.None)));

        _recorded.Should().HaveCount(Deaths, "one record per death, whichever start won each claim");
        _recorded.Select(n => n.Subject).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AClearRemovesThisRunsMarker_AndNoOneElses()
    {
        var markers = Markers(Me);
        markers.Write();
        Leave(Marker("someone00000", silentFor: TimeSpan.FromMinutes(1)));

        markers.Clear();

        Left().Should().Equal(["someone00000.json"]);
    }

    [Fact]
    public void ATemporaryFileLeftByAWriteThatDied_IsRetired_AndIsNotADeath()
    {
        Directory.CreateDirectory(Runs);
        var torn = Path.Combine(Runs, "torn00000000.json.tmp");
        File.WriteAllText(torn, "{\"run\":");
        File.SetLastWriteTimeUtc(torn, Now - AnHour);

        Markers(Me).Sweep(Landing, CancellationToken.None);

        File.Exists(torn).Should().BeFalse("the growth budget: every start removes every stale file it finds");
        _recorded.Should().BeEmpty("a write that died before its first marker landed is not a death anyone can name");
    }

    [Fact]
    public void AFileThatIsNotOurs_IsLeftAlone()
    {
        Directory.CreateDirectory(Runs);
        var stranger = Path.Combine(Runs, "README.txt");
        File.WriteAllText(stranger, "hello");
        File.SetLastWriteTimeUtc(stranger, Now - AnHour);

        Markers(Me).Sweep(Landing, CancellationToken.None);

        File.Exists(stranger).Should().BeTrue("the sweep removes its own kinds of file and nothing else");
    }

    [Fact]
    public void EveryNoticeThisRunWrites_CarriesItsRunAndPid()
    {
        // Defect 3 of the plan: `ServerNotice.Run` existed — "an id minted once per host start" — and no
        // producer set it, so the page could not tell which run a record came from and a death record
        // had nothing to join to. Stamped in ONE place, the host's Noticing, rather than at every site.
        var written = new ConcurrentQueue<ServerNotice>();
        var writer = new NoticeWriter((_, notice) =>
        {
            written.Enqueue(notice);

            return true;
        });
        var noticing = Noticing.Through(
            writer, name => name == "COAI_DATA_DIR" ? _dir.Path : null, Serilog.Core.Logger.None, "thisrun00000");

        noticing.Offered(() => new ServerNotice
        {
            Utc = ServerNotice.Iso(Now),
            Class = "refusal",
            Source = "coai-mcp",
            Code = ServerNoticeCodes.Refused,
            Title = "no reviewers are configured",
        });
        writer.Drain(TimeSpan.FromSeconds(10));

        var notice = written.Should().ContainSingle().Subject;
        notice.Run.Should().Be("thisrun00000", "the id this run's marker is named for");
        notice.Pid.Should().Be(Environment.ProcessId);
    }

    [Fact]
    public void ARecordAboutAnotherRun_KeepsThatRunsId()
    {
        // The stamp fills what is absent and overwrites nothing: an unclean-exit record is ABOUT a dead
        // run, and must keep that run's id and pid rather than take the writer's.
        var written = new ConcurrentQueue<ServerNotice>();
        var writer = new NoticeWriter((_, notice) =>
        {
            written.Enqueue(notice);

            return true;
        });
        var noticing = Noticing.Through(
            writer, name => name == "COAI_DATA_DIR" ? _dir.Path : null, Serilog.Core.Logger.None, "thisrun00000");

        noticing.Offered(() => UncleanExit.Of(Marker("deadrun00000", pid: 777, silentFor: AnHour), Now));
        writer.Drain(TimeSpan.FromSeconds(10));

        var notice = written.Should().ContainSingle().Subject;
        notice.Run.Should().Be("deadrun00000");
        notice.Pid.Should().Be(777);
    }

    [Fact]
    public void TheDeathRecord_NamesTheDeadRun_AndIsItsOwnRow()
    {
        var dead = Marker("named0000000", silentFor: AnHour);

        var notice = UncleanExit.Of(dead, Now);

        notice.Code.Should().Be(ServerNoticeCodes.UncleanExit);
        notice.Class.Should().Be("failure");
        notice.Subject.Should().Be(dead.Run, "each death its own row; a duplicate lands in the SAME row");
        notice.Run.Should().Be(dead.Run, "the record is about that run, and joins its other records");
        notice.Pid.Should().Be(dead.Pid);
        notice.Title.Should().Contain(dead.Host).And.Contain(ServerNotice.Iso(dead.HeartbeatUtc));
    }

    /// <remarks>
    /// Held HERE, because the extension's scan cannot see it: <c>theInventoryIsComplete</c> finds a
    /// path composed as <c>Path.Combine(dataDir, "literal")</c>, and this folder is composed from a
    /// CONSTANT on <c>dataDir.Path</c> — invisible to it, and so a folder in every data directory that
    /// the panel's move instructions would never have heard of. (The plan's correction 6 named it.)
    /// </remarks>
    [Fact]
    public void TheMarkersFolder_IsInTheDataInventory_AndStaysBehindOnAMove()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "data-inventory.json"));
        using var inventory = JsonDocument.Parse(File.ReadAllText(path));

        var entry = inventory.RootElement.GetProperty("entries").EnumerateArray()
            .Where(e => e.GetProperty("path").GetString() == RunMarkers.Folder + "/")
            .ToList();

        entry.Should().ContainSingle("every folder under the data directory has a fate, and this one is new");
        entry[0].GetProperty("move").GetBoolean().Should().BeFalse(
            "a copied live marker is recorded at the destination as a death it is not");
    }
}
