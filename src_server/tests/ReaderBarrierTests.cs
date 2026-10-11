using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The barrier in the slot: a previous holder's reader marker is reconciled under the lock before the lease is usable —
/// what it names is killed, pid AND start time matched, and confirmed gone; what it cannot name blocks the slot (story
/// 6.1a of <c>todo/PLAN_every_round_is_counted.md</c>, the epic-6 risk consultation's order).
/// </summary>
/// <remarks>
/// <para>Against REAL processes where a process is what is at stake: the fake CLI's <c>serve</c> verb stands in for a
/// reader a crashed server left behind — it lives for ever and has a sleeping grandchild — and this test is the server
/// that died, writing the marker by hand. The one case no test machine can safely stage, a process that refuses to die,
/// is staged through the registry's reconcile seam.</para>
/// <para>The crash harness (<see cref="GaugeReaderCrashHarnessTests"/>) does the same against the real server binary.</para>
/// </remarks>
public sealed class ReaderBarrierTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-barrier-" + Guid.NewGuid().ToString("N"));

    private readonly List<Process> _started = [];

    private static readonly string FakeCli = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public ReaderBarrierTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "vendors.json"), """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");
        New().MarkSignedIn(New().Read("codex", "a"));
    }

    public void Dispose()
    {
        foreach (var process in _started)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }

            process.Dispose();
        }

        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory a dying child still holds is the OS's to clean.
        }
    }

    [Fact]
    public async Task AMarkerWithNoIdentity_BlocksTheSlot_AndStaysBlockedAcrossARestart()
    {
        Plant(new ReaderMarkerDto(ReaderPhases.Spawning, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow));

        var taken = await New().AcquireAsync(Slot(), TimeSpan.Zero);

        taken.Should().BeOfType<Acquisition.Blocked>().Which.Code.Should().Be(BlockedReasons.ReaderIdentityMissing,
            "the server died between writing the marker and registering the child: a reader may exist and nothing can name it");
        var slot = Slot();
        slot.Blocked.Should().Be(BlockedReasons.ReaderIdentityMissing, "persisted, so a restart reads it");
        slot.IsReady(DateTimeOffset.UtcNow).Should().BeFalse();
        SlotSelector.Pick([slot], DateTimeOffset.UtcNow).Should().BeNull("a blocked account takes no job");

        // A NEW registry over the same directory is what a restarted server has.
        (await New().AcquireAsync(Slot(), TimeSpan.Zero)).Should().BeOfType<Acquisition.Blocked>("availability is the price of an ambiguous crash");
        File.Exists(Path.Combine(Slot().Directory, ".lock")).Should().BeTrue();
        (await New().AcquireAsync(Slot(), TimeSpan.Zero)).Should().BeOfType<Acquisition.Blocked>("the handle was closed again: a blocked slot is not a held one");
    }

    [Fact]
    public async Task APartialRecord_BlocksTheSlot()
    {
        Plant(new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow, [new ProcessIdentity(0, default)]));

        (await New().AcquireAsync(Slot(), TimeSpan.Zero)).Should().BeOfType<Acquisition.Blocked>()
            .Which.Code.Should().Be(BlockedReasons.ReaderIdentityMissing, "a record with no start time cannot be matched against anything");
        Slot().Blocked.Should().Be(BlockedReasons.ReaderIdentityMissing);
    }

    [Fact]
    public async Task AMarkerNamingALiveReader_KillsItsWholeTree_ConfirmsTheExit_ThenHandsTheLeaseOut()
    {
        Assert.SkipUnless(ProcessTree.Supported, "a tree is confirmed on Linux and Windows only");
        var (reader, grandchild) = await LeftBehindAsync();
        Plant(new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow, [reader]));

        var taken = await New().AcquireAsync(Slot(), TimeSpan.Zero);

        taken.Should().BeOfType<Acquisition.Taken>("the reader was killed and confirmed gone before the lease was usable");
        ProcessTree.IsAlive(reader).Should().BeFalse();
        ProcessTree.IsAlive(grandchild).Should().BeFalse("the whole tree, not the root alone");
        Slot().Blocked.Should().BeEmpty();
        State().Reader.Should().BeNull("cleared on confirmation");
        ((Acquisition.Taken)taken).Lease.Dispose();
    }

    [Fact]
    public async Task AMarkerNamingAGoneReader_Clears()
    {
        var gone = await ExitedAsync();
        Plant(new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow, [gone]));

        var taken = await New().AcquireAsync(Slot(), TimeSpan.Zero);

        taken.Should().BeOfType<Acquisition.Taken>();
        State().Reader.Should().BeNull();
        ((Acquisition.Taken)taken).Lease.Dispose();
    }

    [Fact]
    public async Task AMismatchedStartTime_IsSomebodyElsesProcess_AndIsNotTouched()
    {
        var (bystander, _) = await LeftBehindAsync(grandchild: false);
        var reused = bystander with { StartedUtc = bystander.StartedUtc.AddHours(-1) };
        Plant(new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow, [reused]));

        var taken = await New().AcquireAsync(Slot(), TimeSpan.Zero);

        taken.Should().BeOfType<Acquisition.Taken>("a number that belongs to somebody else now is nothing of ours to kill");
        ProcessTree.IsAlive(bystander).Should().BeTrue("the process under the reused number was left alone");
        ((Acquisition.Taken)taken).Lease.Dispose();
    }

    [Fact]
    public async Task AFailedTermination_KeepsTheSlotUnavailable_AcrossARestart()
    {
        var stubborn = new ProcessIdentity(424243, new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc));
        Plant(new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow, [stubborn]));
        // A kill that never takes: no test machine can make such a process safely, so the reconcile is handed one.
        Task<Reconciled> KillsFail(ReaderMarkerDto marker) =>
            ReaderReconcile.RunAsync(marker, _ => false, members => Task.FromResult(members));

        (await New(KillsFail).AcquireAsync(Slot(), TimeSpan.Zero)).Should().BeOfType<Acquisition.Blocked>()
            .Which.Code.Should().Be(BlockedReasons.ReaderTerminationFailed);
        Slot().Blocked.Should().Be(BlockedReasons.ReaderTerminationFailed);
        State().Reader.Should().NotBeNull("the marker is KEPT for the next reconcile");

        (await New(KillsFail).AcquireAsync(Slot(), TimeSpan.Zero)).Should().BeOfType<Acquisition.Blocked>("another restart, still unavailable");

        // The day the member dies by itself, the next acquisition proves it and the slot comes back without a human.
        var taken = await New().AcquireAsync(Slot(), TimeSpan.Zero);
        taken.Should().BeOfType<Acquisition.Taken>("the recorded member is gone, which the real check can prove");
        Slot().Blocked.Should().BeEmpty();
        ((Acquisition.Taken)taken).Lease.Dispose();
    }

    [Fact]
    public async Task TheStartupSweep_TakesTheSameLockedPath_ForEverySlot()
    {
        Assert.SkipUnless(ProcessTree.Supported, "a tree is confirmed on Linux and Windows only");
        var (reader, grandchild) = await LeftBehindAsync();
        Plant(new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow, [reader]));

        await new GaugeReaderSweep(new VendorCatalogHost(_dir), New(), NullLogger<GaugeReaderSweep>.Instance).StartAsync(CancellationToken.None);

        ProcessTree.IsAlive(reader).Should().BeFalse("the sweep acquired the slot, and the acquisition reconciled it");
        ProcessTree.IsAlive(grandchild).Should().BeFalse();
        State().Reader.Should().BeNull();
        (await New().AcquireAsync(Slot(), TimeSpan.Zero)).Should().BeOfType<Acquisition.Taken>("released again after the sweep");
    }

    [Fact]
    public async Task LoginObeysTheBarrier()
    {
        Plant(new ReaderMarkerDto(ReaderPhases.Spawning, 424242, DateTime.UtcNow, DateTimeOffset.UtcNow));

        var (exitCode, output) = await RunLoginAsync();

        exitCode.Should().Be(VendorLogin.BlockedExitCode, "a sign-in rewrites the token set a reader may still be using");
        output.Should().Contain("unavailable").And.Contain(BlockedReasons.ReaderIdentityMissing);
    }

    private SlotRegistry New(Func<ReaderMarkerDto, Task<Reconciled>>? reconcile = null) =>
        new(_dir, new JsonFileStore(), reconcile: reconcile);

    private AccountSlot Slot() => New().Read("codex", "a");

    private SlotStateDto State() =>
        new JsonFileStore().Read(Path.Combine(Slot().Directory, "state.json"), ServerJsonContext.Default.SlotStateDto)!;

    /// <summary>Writes the marker as the dead server would have left it, beside the slot's other state.</summary>
    private void Plant(ReaderMarkerDto marker)
    {
        var path = Path.Combine(Slot().Directory, "state.json");
        var store = new JsonFileStore();
        var current = store.Read(path, ServerJsonContext.Default.SlotStateDto)!;
        store.Write(path, current with { Reader = marker }, ServerJsonContext.Default.SlotStateDto);
    }

    /// <summary>A reader a crashed server left: the fake CLI serving for ever, with a sleeping grandchild when asked.</summary>
    private async Task<(ProcessIdentity Reader, ProcessIdentity Grandchild)> LeftBehindAsync(bool grandchild = true)
    {
        var pids = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".pids");
        var start = new ProcessStartInfo(FakeCli) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "serve", pids, "-1", grandchild ? "grandchild" : "alone", "{\"id\":2}" })
        {
            start.ArgumentList.Add(arg);
        }

        var process = Process.Start(start)!;
        _started.Add(process);
        _ = process.StandardOutput.ReadToEndAsync();
        var until = DateTime.UtcNow.AddSeconds(30);
        var lines = Array.Empty<string>();
        while (lines.Length != (grandchild ? 2 : 1) && DateTime.UtcNow < until)
        {
            await Task.Delay(50);
            lines = PidsIn(pids);
        }

        lines.Length.Should().Be(grandchild ? 2 : 1, "the reader did not write its pids within 30 s");
        var root = new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime());
        var child = grandchild ? Identity(int.Parse(lines[1])) : root;

        return (root, child);
    }

    /// <summary>The pid file's lines — or none while its writer still holds it, which on Windows is a sharing violation rather than a short read.</summary>
    private static string[] PidsIn(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path) : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static ProcessIdentity Identity(int pid)
    {
        using var process = Process.GetProcessById(pid);

        return new ProcessIdentity(pid, process.StartTime.ToUniversalTime());
    }

    /// <summary>A process that ran and exited — an identity nothing live answers to.</summary>
    private static async Task<ProcessIdentity> ExitedAsync()
    {
        using var process = Process.Start(new ProcessStartInfo(FakeCli) { ArgumentList = { "emit", "x" }, UseShellExecute = false, RedirectStandardOutput = true })!;
        var identity = new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime());
        await process.WaitForExitAsync();

        return identity;
    }

    /// <summary>Runs the real server executable as <c>login</c>, the way `docker compose exec` does — against this directory.</summary>
    private async Task<(int ExitCode, string Output)> RunLoginAsync()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "coai-server" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("login");
        start.ArgumentList.Add("codex");
        start.ArgumentList.Add("a");
        start.Environment["Coai__DataDir"] = _dir;
        start.Environment["Coai__LoginWaitSeconds"] = "1";
        start.Environment["Coai__LoginTimeoutSeconds"] = "2";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdout + await stderr);
    }
}
