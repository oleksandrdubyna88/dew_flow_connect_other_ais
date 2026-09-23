using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An exception nothing else caught is written down, said REDACTED, flushed, and the exit is non-zero.
/// </summary>
/// <remarks>
/// <para><b>What was broken.</b> <c>Main</c> had no <c>try</c> and <c>ServeAsync</c> caught two types,
/// so any third exception escaped the process and the runtime printed it, unredacted, to the stderr an
/// MCP client keeps as this server's log — and the page never heard of it. (Story 3.2; defect 4.)</para>
/// <para>Two layers, because there are two moments: inside <c>ServeAsync</c> there is a logger and a
/// data directory, so the crash is logged redacted and written as a notice; before them — a data
/// directory that cannot be resolved is exactly such a throw — there is only stderr, and <c>Main</c>'s
/// last-resort catch says it there, redacted, and exits 70 all the same.</para>
/// </remarks>
[Collection("fakecli-env")] // the server child inherits our env; keep FAKECLI_* quiet around it
public sealed class ACrashIsWrittenDownTests : IDisposable
{
    /// <summary>A vendor-key shape the redactor knows, whose TAIL must never be seen again.</summary>
    private const string Tail = "abcdefghijklmnopqrstuvwxyz0123456789ABCD";

    private const string Secret = "sk-ant-api03-" + Tail;

    private readonly TempDir _dir = TempDir.For("coai-crash-");

    public void Dispose() => _dir.Dispose();

    private sealed class Boom(string message) : Exception(message);

    private static Serilog.Core.Logger Watching(List<Serilog.Events.LogEvent> said) =>
        new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new Collecting(said)).CreateLogger();

    private sealed class Collecting(List<Serilog.Events.LogEvent> said) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            lock (said)
            {
                said.Add(logEvent);
            }
        }
    }

    private static Exception Thrown()
    {
        try
        {
            throw new Boom($"the vendor refused Authorization: Bearer {Secret}");
        }
        catch (Boom caught)
        {
            return caught; // thrown for real, so it carries frames
        }
    }

    // ---------- before there is a logger: Main's last resort, through the real binary ----------

    [Fact]
    public async Task AThrowBeforeTheLogExists_IsSaidRedacted_AndExitsSeventy()
    {
        // An unusable side throws while the data directory is resolved — before the logger, which is
        // rooted in that very directory — and its message quotes the value it refused.
        var info = new ProcessStartInfo(ServerBinary.Path)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.Environment["COAI_DATA_DIR"] = _dir.Path;
        info.Environment["COAI_DATA_SIDE"] = $"token {Secret}";
        info.Environment["COAI_TRANSLATOR_PROVIDER"] = "none";

        using var server = Process.Start(info)!;
        var stderr = server.StandardError.ReadToEndAsync();
        var stdout = server.StandardOutput.ReadToEndAsync();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await server.WaitForExitAsync(budget.Token);
        var said = await stderr;

        server.ExitCode.Should().Be(HostCrash.ExitCode, $"an unexpected exception is EX_SOFTWARE, not the runtime's own code; stderr: {said}");
        said.Should().NotContain(Tail, "stderr is the log an MCP client keeps, and a secret there is a secret in a file");
        said.Should().Contain(Runners.Reviewers.ShimNotes.Prefix).And.Contain("COAI_DATA_SIDE",
            "what went wrong is still said — only the secret is taken out of it");
        (await stdout).Should().BeEmpty("stdout is the protocol's, even on the way out");
    }

    [Fact]
    public void TheLastResort_WithholdsTheMessage_WhenTheRedactorItselfCannotRun()
    {
        var stderr = new StringWriter();

        var code = HostCrash.Unlogged(Thrown(), stderr, _ => throw new InvalidOperationException("no word list"));

        code.Should().Be(HostCrash.ExitCode);
        stderr.ToString().Should().Contain(typeof(Boom).FullName!, "the TYPE is safe to say whatever else fails")
            .And.NotContain(Tail, "an unredacted message is never the fallback for a redactor that failed");
    }

    // ---------- inside ServeAsync: logged redacted, as a string ----------

    [Fact]
    public void TheCrashIsLoggedRedacted_AndTheExceptionObjectNeverReachesTheLogger()
    {
        var said = new List<Serilog.Events.LogEvent>();
        using var log = Watching(said);

        var code = HostCrash.Handled(Thrown(), log);

        code.Should().Be(HostCrash.ExitCode);
        var line = said.Should().ContainSingle().Subject;
        line.Level.Should().Be(Serilog.Events.LogEventLevel.Error);
        line.Exception.Should().BeNull("a sink renders an exception OBJECT raw — the secret the notice took out");
        line.RenderMessage().Should().NotContain(Tail).And.Contain(nameof(Boom), "and still says what happened");
        line.RenderMessage().Should().Contain(nameof(Thrown), "with its frames");
    }

    // ---------- the notice ----------

    [Fact]
    public void TheCrashNotice_OnDisk_IsRedacted_AndNamedByItsType()
    {
        var dir = PanelSettings.DataDirectoryFor(name => name == "COAI_DATA_DIR" ? _dir.Path : null);

        ServerNotices.Append(dir, HostCrash.Of(Thrown(), DateTime.UtcNow)).Should().BeTrue();

        var bytes = File.ReadAllText(Path.Combine(_dir.Path, ServerNotices.Name));
        bytes.Should().NotContain(Tail, "the notice is redacted at the line, like every notice");
        var line = JsonDocument.Parse(bytes.Trim()).RootElement;
        line.GetProperty("code").GetString().Should().Be(ServerNoticeCodes.Crash);
        line.GetProperty("class").GetString().Should().Be("failure");
        line.GetProperty("subject").GetString().Should().Be(typeof(Boom).FullName,
            "a server that keeps dying the same way is one row with a count");
        line.GetProperty("detail").GetString().Should().Contain(nameof(Thrown), "the frames are the point of a crash record");
    }

    [Fact]
    public void ARecordedCrash_IsOneThatLanded()
    {
        var crash = HostCrash.Of(Thrown(), DateTime.UtcNow);
        var budget = TimeSpan.FromSeconds(5);

        HostCrash.Recorded(crash, _ => true, budget).Should().BeTrue();
        HostCrash.Recorded(crash, _ => false, budget).Should().BeFalse("the append said it did not land");
        HostCrash.Recorded(crash, _ => throw new IOException("the share went away"), budget).Should().BeFalse();
    }

    [Fact]
    public void AWedgedShare_DoesNotKeepADyingProcessAlive()
    {
        using var never = new ManualResetEventSlim();
        var waited = Stopwatch.StartNew();

        var recorded = HostCrash.Recorded(HostCrash.Of(Thrown(), DateTime.UtcNow), _ => never.Wait(TimeSpan.FromSeconds(30)),
            TimeSpan.FromMilliseconds(200));

        recorded.Should().BeFalse("unknown is not landed");
        waited.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the wait is bounded by the budget");
        never.Set();
    }

    [Fact]
    public void AFlushThatThrows_DoesNotReplaceTheCrash()
    {
        var flushing = () => HostCrash.Flushed(new ThrowsOnDispose());

        flushing.Should().NotThrow();
    }

    private sealed class ThrowsOnDispose : IDisposable
    {
        public void Dispose() => throw new IOException("the log's disk is full");
    }

    // ---------- the end of a run: when the marker may be cleared ----------

    private RunLife Living(string run, Func<ServerNotice, bool> recorded) =>
        RunLife.Start(
            new RunMarkers(
                PanelSettings.DataDirectoryFor(name => name == "COAI_DATA_DIR" ? _dir.Path : null),
                new RunMarker(run, Environment.ProcessId, "this-machine", DateTime.UtcNow, DateTime.UtcNow),
                () => DateTime.UtcNow,
                (_, _) => true,
                Serilog.Core.Logger.None),
            recorded,
            Serilog.Core.Logger.None);

    private string MarkerOf(string run) => Path.Combine(_dir.Path, RunMarkers.Folder, run + ".json");

    [Fact]
    public async Task ABeatThatMeetsAnUnexpectedException_SaysSo_AndTheLoopLivesOn()
    {
        // A beat that died in silence is the failure the sweep's catch-all exists to prevent: the run
        // goes on serving, its marker goes stale, and half an hour later a peer records a death that
        // is not one. `Write` names the disk's two failures; anything else must not end the loop.
        var said = new List<Serilog.Events.LogEvent>();
        using var log = Watching(said);
        var life = RunLife.Start(
            new RunMarkers(
                PanelSettings.DataDirectoryFor(name => name == "COAI_DATA_DIR" ? _dir.Path : null),
                new RunMarker("beatbroke000", Environment.ProcessId, "this-machine", DateTime.UtcNow, DateTime.UtcNow),
                () => throw new InvalidOperationException("the clock is not there"),
                (_, _) => true,
                log),
            _ => true,
            log);

        var stopped = await life.StopAsync();

        stopped.Should().BeTrue();
        said.Where(e => e.Level == Serilog.Events.LogEventLevel.Warning)
            .Select(e => e.RenderMessage())
            .Should().Contain(line => line.Contains("heartbeat", StringComparison.Ordinal),
                "a beat that failed is said out loud, not lost with the loop");
    }

    [Fact]
    public async Task ACleanEnd_ClearsTheMarker()
    {
        var life = Living("clean0000000", _ => true);

        await Program.EndedAsync(life, new NoticeWriter((_, _) => true), crash: null, "clean0000000", _ => true, Watching([]));

        File.Exists(MarkerOf("clean0000000")).Should().BeFalse("a run that ended cleanly leaves nothing to record");
    }

    [Fact]
    public async Task ACrashThatLanded_ClearsTheMarker_AndIsStampedAsThisRun()
    {
        var written = new List<ServerNotice>();
        var life = Living("landed000000", _ => true);

        await Program.EndedAsync(life, new NoticeWriter((_, _) => true), Thrown(), "landed000000",
            notice => { written.Add(notice); return true; }, Watching([]));

        File.Exists(MarkerOf("landed000000")).Should().BeFalse("the crash is on disk; recording a death too would say it twice");
        var crash = written.Should().ContainSingle().Subject;
        crash.Code.Should().Be(ServerNoticeCodes.Crash);
        crash.Run.Should().Be("landed000000", "the crash joins this run's other records");
        crash.Pid.Should().Be(Environment.ProcessId);
    }

    [Fact]
    public async Task ACrashThatDidNotLand_LeavesTheMarker_ForTheNextStartToRecord()
    {
        var life = Living("unlanded0000", _ => true);

        await Program.EndedAsync(life, new NoticeWriter((_, _) => true), Thrown(), "unlanded0000", _ => false, Watching([]));

        File.Exists(MarkerOf("unlanded0000")).Should().BeTrue(
            "a marker cleared over a crash that was never written is a death that disappears");
    }
}
