using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Files;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The lease, the generation and the sweep of a role job (epoch 3 E1 s1, scenarios A2 to A5 and A16 of <c>v2/epoch-3/e1-role-jobs-ba/SPEC.md</c>). The job FILES are the authority; a job is <c>lost</c> only
/// when its OS lock is free, and a stale heartbeat is <c>stalled</c>, never <c>lost</c>.
/// </summary>
/// <remarks>
/// <para><b>The holder is a real process.</b> The kernel frees a lock when its holder dies, and that — not a pid, not a timestamp — is what these tests stand on, so a test kills a real holder (A4) and stops a
/// real one (A5) instead of simulating either. They run FakeCli's <c>lease-hold</c> verb, which takes the product's own <c>HeldFile.TryHold</c>.</para>
/// <para>In the serial <c>fakecli-env</c> collection for the reason <see cref="EngineLeaseTests"/> gives: FakeCli reads its argv verbs only while <c>FAKECLI_MODE</c> is unset.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ARoleJobLeaseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    private static RoleJobRecord Terminal(RoleJobRecord job, string state, DateTime endedUtc) => job with { State = state, EndedUtc = endedUtc };

    // ---- A2: two takers race — one wins, the generation moves once ----

    [Fact]
    public async Task A2_TwoTakersRace_OneWins_AndTheGenerationMovesOnce()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob();
        using var gate = new Barrier(8);

        var takers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            gate.SignalAndWait();

            return world.Store.TryTake(job.JobId);
        })).ToArray();
        var taken = await Task.WhenAll(takers);

        try
        {
            taken.Where(lease => lease is not null).Should().ContainSingle("the lease is one file held exclusively: of eight takers, one gets it");
            world.Store.Read(job.JobId)!.Generation.Should().Be(1, "the winner added one to the generation, once");
            taken.Single(lease => lease is not null)!.Generation.Should().Be(1);
        }
        finally
        {
            foreach (var lease in taken)
            {
                lease?.Dispose();
            }
        }

        using var next = world.Store.TryTake(job.JobId);
        next.Should().NotBeNull("the winner let go");
        next!.Generation.Should().Be(2, "a taker after a release is one more taking");
    }

    [Fact]
    public void A2_AHolderInAnotherProcess_KeepsTheLease_AndTheGenerationStill()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob();
        using var holder = world.HoldLease(job.JobId);

        world.Store.TryTake(job.JobId).Should().BeNull("another process holds the lease file");
        world.Store.Read(job.JobId)!.Generation.Should().Be(0, "a taker that did not get the lease changes nothing");
        world.Store.Observe(job.JobId)!.LeaseHeld.Should().BeTrue();
    }

    [Fact]
    public void A2_ATerminalJobCannotBeTakenAgain()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob();
        using (var lease = world.Store.TryTake(job.JobId)!)
        {
            lease.Write(current => Terminal(current, RoleJobStates.Succeeded, DateTime.UtcNow)).Should().BeOfType<RoleJobWrite.Written>();
        }

        world.Store.TryTake(job.JobId).Should().BeNull("a terminal state is final");
        world.Store.Read(job.JobId)!.Generation.Should().Be(1);
    }

    // ---- A3: a stale generation is refused — and the file is unchanged ----

    [Fact]
    public void A3_AStaleHolder_IsRefusedEveryWrite_AndNothingOnDiskChanges()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob();
        using var lease = world.Store.TryTake(job.JobId)!;
        var folder = world.Store.FolderOf(job.JobId)!;
        world.Store.BumpGeneration(job.JobId).Should().Be(2, "a cancel fences the holder without taking the lease");
        var before = File.ReadAllText(folder.JobFile);

        lease.Write(current => current with { State = RoleJobStates.Running }).Should().BeEquivalentTo(new RoleJobWrite.Stale(2));
        lease.Publish("""{"late":true}""", current => Terminal(current, RoleJobStates.Succeeded, DateTime.UtcNow)).Should().BeEquivalentTo(new RoleJobWrite.Stale(2));
        lease.Beat("running", 1, 0).Should().BeFalse("the heartbeat compares too");
        lease.IsCurrent().Should().BeFalse();

        File.ReadAllText(folder.JobFile).Should().Be(before, "a refused write leaves the file byte for byte as it was");
        File.Exists(folder.ResultFile).Should().BeFalse("a stale holder publishes nothing");
        File.Exists(folder.HeartbeatFile).Should().BeFalse();
    }

    [Fact]
    public void A3_TheCurrentHolder_WritesAndPublishes_UnderItsOwnGeneration()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob();
        using var lease = world.Store.TryTake(job.JobId)!;

        lease.IsCurrent().Should().BeTrue();
        lease.Beat("running", 1, 4242).Should().BeTrue();
        world.Store.FolderOf(job.JobId)!.ReadHeartbeat().Should().BeEquivalentTo(new RoleHeartbeat(1, world.Clock.GetUtcNow().UtcDateTime, "running", 1, 4242), options => options.Excluding(beat => beat.Utc));
        lease.Publish("""{"ok":true}""", current => Terminal(current, RoleJobStates.Succeeded, DateTime.UtcNow)).Should().BeOfType<RoleJobWrite.Written>();

        world.Store.FolderOf(job.JobId)!.ReadResult().Should().Be("""{"ok":true}""");
        world.Store.Read(job.JobId)!.State.Should().Be(RoleJobStates.Succeeded);
        lease.Write(current => current with { Reason = "again" }).Should().BeOfType<RoleJobWrite.Over>("a terminal state is final, even for the holder");
    }

    [Fact]
    public void A3_AWriteCannotMoveTheGeneration()
    {
        using var world = new RoleJobWorld();
        var job = world.NewJob();
        using var lease = world.Store.TryTake(job.JobId)!;

        lease.Write(current => current with { Generation = 99 }).Should().BeOfType<RoleJobWrite.Written>();

        world.Store.Read(job.JobId)!.Generation.Should().Be(1, "the generation is moved by takers, never by a change function");
    }

    // ---- A4: the holder is killed — lost, the child killed, one notice ----

    [Fact]
    public async Task A4_AHolderKilled_IsLost_ItsChildIsKilled_AndOneNoticeIsWritten()
    {
        var clock = new ManualClock(T0);
        using var world = new RoleJobWorld(clock);
        var job = world.NewJob();
        using var holder = world.HoldLease(job.JobId);
        using var sleeper = world.Sleeper();
        var child = RoleJobWorld.Identity(sleeper);
        world.Store.Change(job.JobId, current => current with { State = RoleJobStates.Running, Runner = RoleJobWorld.Identity(holder), Child = child });

        RoleJobWorld.Kill9(holder);
        world.Store.Observe(job.JobId)!.Dead.Should().BeFalse("the lease is free but the grace has not passed: a runner still starting is not taken for dead");
        clock.Advance(RoleJobLimits.LostGrace + TimeSpan.FromSeconds(1));
        world.Store.Observe(job.JobId)!.Dead.Should().BeTrue("a free lease on a job that is not over, past the grace, is a dead runner");

        await world.Keeper.InspectAsync(job.JobId);

        var after = world.Store.Read(job.JobId)!;
        after.State.Should().Be(RoleJobStates.Lost);
        after.Stop.Should().Be("lost");
        after.Child.Should().BeNull();
        after.Generation.Should().Be(1, "the keeper took the lease through the one door");
        RoleJobWorld.WaitFor(() => !RoleJobWorld.IsAlive(child), "the recorded child to be killed");
        world.NoticeCodes.Should().Equal(ServerNoticeCodes.RoleJobLost);
        world.Notices.Single().Subject.Should().Be(job.JobId);

        await world.Keeper.InspectAsync(job.JobId);
        world.NoticeCodes.Should().Equal([ServerNoticeCodes.RoleJobLost], "a job is lost once");
    }

    [Fact]
    public async Task A4_TwoKeepersLookingAtOneDeadJob_WriteOneNotice()
    {
        var clock = new ManualClock(T0);
        using var world = new RoleJobWorld(clock);
        var job = world.NewJob();
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = new RoleJobKeeper(world.Store, notice => { world.Notices.Enqueue(notice); return true; }, Serilog.Core.Logger.None);

        await Task.WhenAll(world.Keeper.InspectAsync(job.JobId), second.InspectAsync(job.JobId));

        world.NoticeCodes.Should().Equal([ServerNoticeCodes.RoleJobLost], "the lease is the arbiter: of two keepers one takes it and closes the job");
        world.Store.Read(job.JobId)!.State.Should().Be(RoleJobStates.Lost);
    }

    [Fact]
    public async Task A4_AChildThatIsNotTheRecordedOne_IsNeverTouched()
    {
        // The identity is the pid AND the start time: a record whose start time does not match is a stranger that inherited the number.
        var clock = new ManualClock(T0);
        using var world = new RoleJobWorld(clock);
        var job = world.NewJob();
        using var sleeper = world.Sleeper();
        var wrong = RoleJobWorld.Identity(sleeper) with { StartedUtc = DateTime.UtcNow.AddDays(-3) };
        world.Store.Change(job.JobId, current => current with { State = RoleJobStates.Running, Child = wrong });
        clock.Advance(TimeSpan.FromMinutes(5));

        await world.Keeper.InspectAsync(job.JobId);

        world.Store.Read(job.JobId)!.State.Should().Be(RoleJobStates.Lost);
        RoleJobWorld.IsAlive(RoleJobWorld.Identity(sleeper)).Should().BeTrue("a process whose start time is not the recorded one is not ours to kill");
    }

    // ---- A5: the holder is stopped for three minutes — stalled, never lost ----

    [Fact]
    public async Task A5_AHolderStoppedForThreeMinutes_IsStalled_NeverLost_WithOneNoticePerStall()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "SIGSTOP and SIGCONT are POSIX signals");
        var clock = new ManualClock(T0);
        using var world = new RoleJobWorld(clock);
        var job = world.NewJob();
        using var holder = world.HoldLease(job.JobId);
        world.Store.Change(job.JobId, current => current with { State = RoleJobStates.Running, Runner = RoleJobWorld.Identity(holder) });
        RoleJobWorld.Signal("STOP", holder);

        clock.Advance(TimeSpan.FromMinutes(3));
        var seen = (await world.Keeper.InspectAsync(job.JobId))!;

        seen.LeaseHeld.Should().BeTrue("the stopped process still holds the lease");
        seen.Stalled.Should().BeTrue();
        seen.Dead.Should().BeFalse("a stale heartbeat is not a death: only a free lock is");
        world.Store.Read(job.JobId)!.State.Should().Be(RoleJobStates.Running, "a stalled runner's job is not taken over");
        world.Store.Read(job.JobId)!.Generation.Should().Be(0);
        world.NoticeCodes.Should().Equal(ServerNoticeCodes.RoleJobStalled);

        await world.Keeper.InspectAsync(job.JobId);
        world.NoticeCodes.Should().Equal([ServerNoticeCodes.RoleJobStalled], "one notice per stall, not one per look");

        // It wakes and beats: the stall is over, and the next one is a new stall with its own notice.
        RoleJobWorld.Signal("CONT", holder);
        world.Store.FolderOf(job.JobId)!.WriteHeartbeat(new RoleHeartbeat(0, clock.GetUtcNow().UtcDateTime, "running", 1, 0));
        (await world.Keeper.InspectAsync(job.JobId))!.Stalled.Should().BeFalse();
        clock.Advance(TimeSpan.FromMinutes(3));
        await world.Keeper.InspectAsync(job.JobId);
        world.NoticeCodes.Should().Equal(ServerNoticeCodes.RoleJobStalled, ServerNoticeCodes.RoleJobStalled);
    }

    [Theory]
    [InlineData(true, 119, false, false)]
    [InlineData(true, 121, true, false)]
    [InlineData(true, 3600, true, false)]
    [InlineData(false, 20, false, false)]
    [InlineData(false, 31, false, true)]
    [InlineData(false, 3600, false, true)]
    public void A5_TheJudgement_IsAPureFunctionOfTheLeaseAndTheSilence(bool leaseHeld, int silentSeconds, bool stalled, bool dead)
    {
        var now = T0.UtcDateTime;
        var job = new RoleJobRecord { JobId = "r20261123090000-00000000", State = RoleJobStates.Running, UpdatedUtc = now.AddSeconds(-silentSeconds) };

        var seen = RoleJobLiveness.Judge(job, heartbeat: null, leaseHeld, now);

        (seen.Stalled, seen.Dead).Should().Be((stalled, dead));
    }

    [Fact]
    public void A5_AHeartbeatOfAnOlderGeneration_IsNotASignOfLife_AndANewerOneIs()
    {
        var now = T0.UtcDateTime;
        var job = new RoleJobRecord { State = RoleJobStates.Running, Generation = 2, UpdatedUtc = now.AddMinutes(-10) };

        RoleJobLiveness.Judge(job, new RoleHeartbeat(2, now.AddSeconds(-5), "running", 1, 0), true, now).Stalled.Should().BeFalse("a fresh beat of this generation");
        RoleJobLiveness.Judge(job, null, true, now).Stalled.Should().BeTrue("no beat of this generation, ten minutes since the take-over");
        RoleJobLiveness.Judge(job with { State = RoleJobStates.Succeeded }, null, false, now).Should().BeEquivalentTo(new { Stalled = false, Dead = false }, options => options.ExcludingMissingMembers());
    }

    // ---- A16: retention ----

    [Fact]
    public void A16_ATerminalFolderGoesAfterThirtyDays_AndOneThatIsNotOverNever()
    {
        var clock = new ManualClock(T0);
        using var world = new RoleJobWorld(clock);
        var old = Finished(world, T0.UtcDateTime);
        var young = Finished(world, T0.UtcDateTime.AddDays(20));
        var queued = world.NewJob();
        clock.Advance(TimeSpan.FromDays(40));

        var removed = RoleJobRetention.Apply(world.Store, Serilog.Core.Logger.None);

        removed.Should().Equal(old);
        world.Store.Read(old).Should().BeNull();
        world.Store.Read(young).Should().NotBeNull("20 days old is inside the 30");
        world.Store.Read(queued.JobId).Should().NotBeNull("a job that is not over is never removed, however old");
        RoleJobLimits.RetainFor.Should().Be(TimeSpan.FromDays(30));
        RoleJobLimits.RetainAtMost.Should().Be(200);
    }

    [Fact]
    public void A16_AtMostTwoHundredTerminalFoldersAreKept_TheNewest()
    {
        using var world = new RoleJobWorld();
        var ids = Enumerable.Range(0, 205).Select(day => Finished(world, DateTime.UtcNow.AddMinutes(-day))).ToList();

        var removed = RoleJobRetention.Apply(world.Store, Serilog.Core.Logger.None);

        removed.Should().BeEquivalentTo(ids.Skip(200), "the five OLDEST by their end time go");
        world.Store.JobIds().Should().HaveCount(200);
    }

    [Fact]
    public void A16_AFolderWhoseLeaseIsHeld_IsNeverRemoved()
    {
        var clock = new ManualClock(T0);
        using var world = new RoleJobWorld(clock);
        var job = Finished(world, T0.UtcDateTime);
        using var holder = world.HoldLease(job);
        clock.Advance(TimeSpan.FromDays(90));

        RoleJobRetention.Apply(world.Store, Serilog.Core.Logger.None).Should().BeEmpty("a runner still finishing in a folder it marked over is not interrupted");
        world.Store.Read(job).Should().NotBeNull();

        RoleJobWorld.Kill9(holder);
        RoleJobRetention.Apply(world.Store, Serilog.Core.Logger.None).Should().Equal(job);
    }

    [Fact]
    public void A16_ACoderJobsOwnFoldersGoWithItsRecord_AndOnlyAfterThirtyDays()
    {
        var clock = new ManualClock(T0);
        using var world = new RoleJobWorld(clock);
        using var jobs = TempDir.For("coai-coder-jobs-");
        var old = world.NewJob(role: RoleDescriptors.Coder);
        var young = world.NewJob(role: RoleDescriptors.Coder);
        var probe = world.NewJob();
        foreach (var (job, ended) in new[] { (old, T0.UtcDateTime), (young, T0.UtcDateTime.AddDays(20)), (probe, T0.UtcDateTime) })
        {
            using var lease = world.Store.TryTake(job.JobId)!;
            lease.Write(current => Terminal(current, RoleJobStates.Succeeded, ended)).Should().BeOfType<RoleJobWrite.Written>();
            foreach (var name in new[] { job.JobId, job.JobId + ".mounts" })
            {
                Directory.CreateDirectory(Path.Combine(jobs.Path, name));
                File.WriteAllText(Path.Combine(jobs.Path, name, "x.txt"), "x\n");
            }
        }

        clock.Advance(TimeSpan.FromDays(40));
        string? Settings(string name) => name == CoderOsLayer.JobsVariable ? jobs.Path : null;

        var removed = RoleJobRetention.Apply(world.Store, Serilog.Core.Logger.None, Settings);

        removed.Should().BeEquivalentTo([old.JobId, probe.JobId]);
        Directory.Exists(Path.Combine(jobs.Path, old.JobId)).Should().BeFalse("the coder job's record went, and its own folder with it");
        Directory.Exists(Path.Combine(jobs.Path, old.JobId + ".mounts")).Should().BeFalse();
        Directory.Exists(Path.Combine(jobs.Path, young.JobId)).Should().BeTrue("20 days old is inside the 30");
        Directory.Exists(Path.Combine(jobs.Path, young.JobId + ".mounts")).Should().BeTrue();
        Directory.Exists(Path.Combine(jobs.Path, probe.JobId)).Should().BeTrue("a job that is not a coder's has no folder of this kind: nothing is looked for by its id");
    }

    [Fact]
    public void A16_TheRowsStay_WhenTheFolderGoes()
    {
        var clock = new ManualClock(T0);
        using var world = new RoleJobWorld(clock);
        var store = world.ProjectedStore();
        var job = store.Create(new RoleJobRequest { JobId = RoleJobIds.New(clock), Role = "probe", RepoPath = world.DataDir, Brief = "succeed", TimeoutMinutes = 30 });
        using (var lease = store.TryTake(job.JobId)!)
        {
            lease.Write(current => Terminal(current, RoleJobStates.Succeeded, T0.UtcDateTime));
        }

        clock.Advance(TimeSpan.FromDays(31));
        RoleJobRetention.Apply(store, Serilog.Core.Logger.None).Should().Equal(job.JobId);

        store.Read(job.JobId).Should().BeNull();
        RowOf(world, job.JobId).Should().Be((RoleJobStates.Succeeded, 1), "the projection keeps the history the folder no longer holds");
    }

    private static string Finished(RoleJobWorld world, DateTime endedUtc)
    {
        var job = world.NewJob();
        using var lease = world.Store.TryTake(job.JobId)!;
        lease.Write(current => Terminal(current, RoleJobStates.Succeeded, endedUtc)).Should().BeOfType<RoleJobWrite.Written>();

        return job.JobId;
    }

    private static (string State, int Generation)? RowOf(RoleJobWorld world, string jobId)
    {
        try
        {
            using var db = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, "coai.db")};Pooling=False;Mode=ReadOnly");
            db.Open();
            using var ask = db.CreateCommand();
            ask.CommandText = "SELECT state, generation FROM role_jobs WHERE job_id = $id";
            ask.Parameters.AddWithValue("$id", jobId);
            using var reader = ask.ExecuteReader();

            return reader.Read() ? (reader.GetString(0), reader.GetInt32(1)) : null;
        }
        catch (SqliteException e)
        {
            throw new InvalidOperationException($"the rounds database has no row to read ({e.Message}); the log said: {world.LogText}", e);
        }
    }

    // ---- the projection: the files are the authority, the database a view ----

    [Fact]
    public void TheProjection_FollowsTheJobFile_RowByRow()
    {
        using var world = new RoleJobWorld();
        var store = world.ProjectedStore();
        var job = store.Create(new RoleJobRequest { JobId = RoleJobIds.New(world.Clock), Role = "probe", RepoPath = world.DataDir, Brief = "succeed", TimeoutMinutes = 30, WorkItem = "v2/epoch-3" });

        RowOf(world, job.JobId).Should().Be((RoleJobStates.Queued, 0));
        using var lease = store.TryTake(job.JobId)!;
        RowOf(world, job.JobId).Should().Be((RoleJobStates.Queued, 1), "the take-over is a write");
        lease.Write(current => current with
        {
            State = RoleJobStates.Failed,
            Stop = "crash",
            Reason = "x",
            Attempts = [new RoleAttempt(1, "fable", "claude", "crash", "boom", "attempts/1") { TokensIn = 10, TokensOut = 20 }],
        });

        using var db = new SqliteConnection($"Data Source={Path.Combine(world.DataDir, "coai.db")};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var ask = db.CreateCommand();
        ask.CommandText = "SELECT j.state, j.attempts, j.stop, j.work_item, a.model, a.vendor, a.tokens_out FROM role_jobs j JOIN role_attempts a ON a.job_id = j.job_id";
        using var row = ask.ExecuteReader();
        row.Read().Should().BeTrue();
        (row.GetString(0), row.GetInt32(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5), row.GetInt64(6))
            .Should().Be((RoleJobStates.Failed, 1, "crash", "v2/epoch-3", "fable", "claude", 20L));
    }

    [Fact]
    public void AnUnwritableDatabase_ChangesNoJobResult()
    {
        using var world = new RoleJobWorld();
        // A folder where the database file should be: the database cannot be opened, whatever anybody does.
        Directory.CreateDirectory(Path.Combine(world.DataDir, "coai.db"));
        var store = world.ProjectedStore();
        var job = store.Create(new RoleJobRequest { JobId = RoleJobIds.New(world.Clock), Role = "probe", RepoPath = world.DataDir, Brief = "succeed", TimeoutMinutes = 30 });

        using var lease = store.TryTake(job.JobId)!;
        lease.Publish("""{"ok":true}""", current => Terminal(current, RoleJobStates.Succeeded, DateTime.UtcNow)).Should().BeOfType<RoleJobWrite.Written>("a view that cannot be written is never a failed job");

        store.Read(job.JobId)!.State.Should().Be(RoleJobStates.Succeeded);
        store.FolderOf(job.JobId)!.ReadResult().Should().Be("""{"ok":true}""");
    }

    [Fact]
    public void AProjectionThatThrows_IsContained_AndTheWriteStands()
    {
        using var world = new RoleJobWorld();
        var store = new RoleJobStore(world.DataDir, world.Clock, Serilog.Core.Logger.None, _ => throw new InvalidOperationException("the database is on fire"));
        var job = store.Create(new RoleJobRequest { JobId = RoleJobIds.New(world.Clock), Role = "probe", RepoPath = world.DataDir, Brief = "succeed", TimeoutMinutes = 30 });

        using var lease = store.TryTake(job.JobId)!;

        lease.Write(current => current with { State = RoleJobStates.Running }).Should().BeOfType<RoleJobWrite.Written>();
        store.Read(job.JobId)!.State.Should().Be(RoleJobStates.Running);
    }

    // ---- the data directory keeps the folder apart, and ids cannot name another folder ----

    [Fact]
    public void TheRolesFolder_IsInTheDataInventory_AndStaysBehindOnAMove()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "data-inventory.json"));
        using var inventory = JsonDocument.Parse(File.ReadAllText(path));

        var entry = inventory.RootElement.GetProperty("entries").EnumerateArray().Where(e => e.GetProperty("path").GetString() == "roles/").ToList();

        entry.Should().ContainSingle("the jobs folder is new, and every folder under the data directory has a fate");
        entry[0].GetProperty("move").GetBoolean().Should().BeFalse("a live lease and heartbeat mean nothing on another machine");
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("r20261123090000-0000000/")]
    [InlineData("/etc/passwd")]
    [InlineData("r20261123090000-0000000g")]
    [InlineData("R20261123090000-00000000")]
    [InlineData("r20261123090000-00000000 ")]
    [InlineData("")]
    public void AnId_ThatIsNotTheExactShape_NamesNoFolder(string id)
    {
        using var world = new RoleJobWorld();

        RoleJobIds.IsValid(id).Should().BeFalse();
        world.Store.FolderOf(id).Should().BeNull();
        world.Store.Read(id).Should().BeNull();
        world.Store.TryTake(id).Should().BeNull();
        world.Store.BumpGeneration(id).Should().Be(0);
    }

    [Fact]
    public void AFreshId_IsTheExactShape_AndSortsByTime()
    {
        var clock = new ManualClock(T0);
        var first = RoleJobIds.New(clock);
        clock.Advance(TimeSpan.FromSeconds(2));
        var second = RoleJobIds.New(clock);

        RoleJobIds.IsValid(first).Should().BeTrue();
        string.CompareOrdinal(first, second).Should().BeLessThan(0);
        RoleJobIds.UnitOf(first).Should().Be("coai-role-" + first);
    }

    [Fact]
    public void TheEnvironmentFile_IsOwnerOnly_ReadOnce_AndGone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX modes");
        using var world = new RoleJobWorld();
        var job = world.NewJob();
        var folder = world.Store.FolderOf(job.JobId)!;

        folder.WriteEnvironment(new RoleJobEnvironment(new Dictionary<string, string> { ["PATH"] = "/x" }));

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(folder.EnvFile).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        folder.TakeEnvironment()!.Variables.Should().Contain("PATH", "/x");
        File.Exists(folder.EnvFile).Should().BeFalse("deleted after the read");
        folder.TakeEnvironment().Should().BeNull();
    }
}
