using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultation turn that never returns, and a record stuck at <c>asking</c> that nothing could end.
/// </summary>
/// <remarks>
/// <para>Observed live on 2026-09-26. Codex answered a risk consultation in three minutes; the server
/// wrote the answered record's <c>.tmp</c> beside the record and the atomic rename over the record
/// failed. The exception left the turn from a region nothing guarded, so the caller saw <i>An error
/// occurred invoking 'consult'</i>, the record read <c>asking</c> an hour later, <c>close_consult</c>
/// refused it as "still running a turn", and the sweep kept it because the pid it named — the
/// server's own — was alive. The cadence gate then refused the epic's code round for want of a
/// consultation nobody could end.</para>
/// <para>Every test here drives the real service over the fake CLI, with ONE seam: the launcher the
/// service is handed. A decorator of the real one can act at the moment the record has just been
/// marked <c>asking</c> — the only moment the defect can be reproduced without a race.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ATurnThatNeverReturnsTests : ConsultScenarioBase
{
    /// <summary>The real launcher, with one seam: what happens around the CONSULTANT's own launch.</summary>
    private sealed class TurnLauncher(IProcessLauncher inner) : IProcessLauncher
    {
        /// <summary>Runs INSTEAD of the consultant's launch; null runs the fake CLI as usual.</summary>
        public Func<ProcessRequest, CancellationToken, Task<ProcessResult>>? Consultant { get; init; }

        /// <summary>Runs just before the consultant is launched — after the record says <c>asking</c>.</summary>
        public Action? BeforeConsultant { get; set; }

        /// <summary>While set, every git command after the consultant ran is refused.</summary>
        public bool GitRefused { get; set; }

        public bool ConsultantRan { get; private set; }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (!string.Equals(request.Executable, FakeCliExe, StringComparison.OrdinalIgnoreCase))
            {
                return ConsultantRan && GitRefused
                    ? new ProcessResult(128, string.Empty, "fatal: the test made git refuse after the consultant ran", TimedOut: false)
                    : await inner.RunAsync(request, ct);
            }

            BeforeConsultant?.Invoke();
            ConsultantRan = true;

            return Consultant is { } consultant ? await consultant(request, ct) : await inner.RunAsync(request, ct);
        }
    }

    /// <summary>
    /// Makes the record's atomic replacement fail, the way something on the operator's machine did.
    /// </summary>
    /// <remarks>
    /// On Windows the record is held open without delete sharing, so the rename over it is refused —
    /// the ordinary Windows case, and the one <c>SharedRead</c>'s remarks record this data directory
    /// meeting four times. Elsewhere a rename over an open file succeeds, so the directory is made
    /// read-only instead and the temporary file cannot be created: a different door into the same
    /// unguarded region. Either way the settle write fails, and so does any write after it, which is
    /// exactly the state the live record was found in.
    /// </remarks>
    private sealed class RecordBlocker(string consultationsDir) : IDisposable
    {
        private FileStream? _held;
        private UnixFileMode? _was;

        public void Apply()
        {
            var record = Directory.EnumerateFiles(consultationsDir, "*.json").Single();
            if (OperatingSystem.IsWindows())
            {
                _held = new FileStream(record, FileMode.Open, FileAccess.Read, FileShare.Read);

                return;
            }

            _was = File.GetUnixFileMode(consultationsDir);
            File.SetUnixFileMode(consultationsDir,
                UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            // A fixture the code does not reject proves nothing: as root the mode bites nobody.
            var probe = Path.Combine(consultationsDir, "probe.tmp");
            var bites = false;
            try
            {
                File.WriteAllText(probe, "probe");
                File.Delete(probe);
            }
            catch (UnauthorizedAccessException)
            {
                bites = true;
            }

            bites.Should().BeTrue("a read-only directory must refuse the write this test relies on — is the suite running as root?");
        }

        public void Release()
        {
            _held?.Dispose();
            _held = null;
            if (_was is { } was && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(consultationsDir, was);
                _was = null;
            }
        }

        public void Dispose() => Release();
    }

    /// <summary>Moves a record's last activity into the past, as the clock would.</summary>
    private static void Backdate(ConsultationStore store, string id, TimeSpan by)
    {
        var record = store.Read(id)!;
        store.Write(record with { UpdatedUtc = ConsultationStore.Stamp(DateTime.UtcNow - by) });
    }

    /// <summary>A launch that ends only when it is told to — the shape of every wait that "bounds nothing".</summary>
    private static async Task<ProcessResult> NeverOnItsOwn(ProcessRequest request, CancellationToken ct)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);

        throw new UnreachableException("an infinite delay ends only by cancellation");
    }

    /// <summary>
    /// The live incident, reproduced: the consultant answered, the record could not be rewritten.
    /// </summary>
    [Fact]
    public async Task TheRecordWriteFailingAfterTheAnswer_IsASentence_AndTheSweepEndsTheRecord()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var blocker = new RecordBlocker(Path.Combine(_data, "consultations"));
        var launcher = new TurnLauncher(_launcher) { BeforeConsultant = blocker.Apply };
        var service = Service(launcher: launcher);

        var reply = await Consult(service, "the parser returns 3 where 4 is expected");
        blocker.Release();
        launcher.BeforeConsultant = null; // one-shot: the retry below writes its record normally

        // A sentence, never an exception up the stdio stack — the caller has to be able to act on it.
        launcher.ConsultantRan.Should().BeTrue("the consultant ran and answered; the failure is after it");
        reply.TryGetProperty("error", out var error).Should().BeTrue(reply.ToString());
        error.GetString().Should().Contain("could not be recorded");

        // The record is exactly as the live one was found: the settle write failed, and so did the
        // write on the failure path, so it still says `asking` — and its pid is this process, alive.
        var store = new ConsultationStore(_data);
        var record = store.All().Single();
        record.Status.Should().Be(ConsultationStatuses.Asking, "no write could land while the blocker held");
        record.RunnerPid.Should().Be(Environment.ProcessId);

        // The sweep ends it although its server is alive: nobody holds the repository's lock and the
        // turn's deadline is past — which is how the record on the operator's machine clears itself.
        Backdate(store, record.Id, TimeSpan.FromMinutes(70));
        service.SweepConsultations().Should().Be(1, "an asking record past its deadline, with its lock free, is not a running turn");
        var swept = store.Read(record.Id)!;
        swept.Status.Should().Be(ConsultationStatuses.Failed, "no handle reached the record, so nothing can be resumed");
        swept.Reason.Should().Contain("deadline");

        // And the caller can act: the failed consultation takes no outcome, a new one can be asked.
        Refusal(await Close(service, record.Id, "abandoned")).Should().Contain("failed");
        var again = await Consult(service, "the parser returns 3 where 4 is expected, once more");
        again.TryGetProperty("error", out _).Should().BeFalse(again.ToString());
        again.GetProperty("consultationId").GetString().Should().NotBe(record.Id);
    }

    /// <summary>
    /// The region after the launch had no guard at all: git refusing the second snapshot left the
    /// record at <c>asking</c> and answered "no consultant was launched" about one that had run.
    /// </summary>
    [Fact]
    public async Task GitRefusingAfterTheConsultantRan_LeavesAResumableRecord_NotAnAskingOne()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var launcher = new TurnLauncher(_launcher) { GitRefused = true };
        var service = Service(launcher: launcher);

        var reply = await Consult(service, "the parser returns 3 where 4 is expected");

        var record = new ConsultationStore(_data).All().Single();
        record.Status.Should().Be(ConsultationStatuses.Interrupted, "the vendor accepted the turn — its thread id is on the stream — so the conversation is resumable");
        record.Handle.Should().Be("0198f2c1-first");
        record.Turns.Should().BeEmpty("a turn whose answer never reached the record is not counted");
        Refusal(reply).Should().Contain("NOT counted").And.Contain(record.Id);

        // The next call picks the conversation up, and the consultation can then be closed.
        launcher.GitRefused = false;
        var resumed = await Consult(service, "I checked the separator: it is counted once", record.Id);
        resumed.TryGetProperty("error", out _).Should().BeFalse(resumed.ToString());
        Advise(resumed).Should().Contain(Advice);
        (await Close(service, record.Id, "solved")).GetProperty("recorded").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// A launch that ignores its own timeout is ended by the turn's deadline, and the record says so.
    /// </summary>
    [Fact]
    public async Task ALaunchThatNeverEnds_IsEndedByTheTurnsOwnDeadline_AndTheRecordSaysSo()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var launcher = new TurnLauncher(_launcher) { Consultant = NeverOnItsOwn };
        var service = Service(launcher: launcher, reviewerTimeout: TimeSpan.FromSeconds(1));
        var started = Stopwatch.StartNew();

        var reply = await Consult(service, "the parser returns 3 where 4 is expected").WaitAsync(TimeSpan.FromSeconds(30));

        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        Refusal(reply).Should().Contain("deadline");
        var record = new ConsultationStore(_data).All().Single();
        record.Status.Should().Be(ConsultationStatuses.Failed, "nothing was said on the stream, so there is no handle to resume");
        record.Reason.Should().Contain("deadline");
    }

    /// <summary>
    /// The record exactly as the incident left it, swept by the beat that runs while the server serves.
    /// </summary>
    [Fact]
    public async Task TheSweepEndsAnAskingRecordThisServerLeftBehind_OnceItsDeadlineHasPassed()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service();
        var anHourAgo = ConsultationStore.Stamp(DateTime.UtcNow.AddMinutes(-70));
        var stuck = new ConsultationRecord(
            ConsultationStore.NewId(), "caller-1", CallerIdentity.Claude, "no-session", _repo, "main", "0123abc",
            "codex", "gpt-5.6", "codex", ConsultationMemories.VendorRemembers, 5, anHourAgo)
        {
            Status = ConsultationStatuses.Asking,
            RunnerPid = Environment.ProcessId,
            UpdatedUtc = anHourAgo,
            Kind = "risk",
        };
        service.Consultations.Write(stuck);

        service.SweepConsultations().Should().Be(1, "its server is alive, but nobody holds the lock and the deadline is an hour past");

        var swept = service.Consultations.Read(stuck.Id)!;
        swept.Status.Should().Be(ConsultationStatuses.Failed, "no handle reached the record, so nothing can be resumed");
        swept.Reason.Should().Contain("deadline");
        swept.EndedUtc.Should().NotBeEmpty("a failed consultation is over — it is no cadence evidence and blocks no round");
    }

    /// <summary>
    /// A pin, not a fix: the CALLER's own cancellation still surfaces as one, with the record settled.
    /// </summary>
    [Fact]
    public async Task TheCallerCancelling_StillSurfacesAsACancellation_WithTheRecordSettled()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var caller = new CancellationTokenSource();
        var launcher = new TurnLauncher(_launcher)
        {
            // Cancelled from INSIDE the launch, so the token fires after the record says `asking`
            // and never during the git commands before it — a timer would make this a race.
            Consultant = async (request, ct) =>
            {
                await caller.CancelAsync();

                return await NeverOnItsOwn(request, ct);
            },
        };
        var service = Service(launcher: launcher);

        var act = () => service.ConsultAsync(_repo, "the parser returns 3 where 4 is expected", "[]", string.Empty, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("a job the caller withdrew is not a failure of the consultant");
        var record = new ConsultationStore(_data).All().Single();
        record.Status.Should().Be(ConsultationStatuses.Failed);
        record.Reason.Should().Contain("cancelled");
    }
}
