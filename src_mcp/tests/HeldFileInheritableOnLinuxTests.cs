using System.Diagnostics;
using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// L-N1 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c> (DL8; S-K4 and S-K5), on the primitive itself: <c>HeldFile.Take(path, inheritable: true)</c> on Linux. On Unix .NET turns a share mode into an
/// advisory <c>flock</c>; two facts are unproven there — (a) that the combined share mode <c>None | Inheritable</c> still takes an EXCLUSIVE lock, (b) that the descriptor reaches a child started after
/// the take and outlives its opener. Each arm is its own test, so a red names exactly the premise that broke.
/// </summary>
/// <remarks>
/// <para><b>Prediction, from the source and not yet from a run.</b> .NET's <c>SafeFileHandle.Unix.cs</c> (v10.0.11, read 2026-10-16) chooses <c>LOCK_EX</c> only when <c>share == FileShare.None</c> EXACTLY — otherwise
/// <c>LOCK_SH</c> — and adds <c>O_CLOEXEC</c> only when <c>Inheritable</c> is absent. So (a) is predicted to FAIL on Linux — an inheritable take is a shared lock: a second INHERITABLE take also succeeds,
/// while a non-inheritable one (<c>LOCK_EX</c>) is refused — and (b) to hold. The ubuntu CI job is the observation; the decision it feeds (the native take, or the pinned remark) is A2 §3.3's, not this file's.</para>
/// <para>The tree hold of the product, <c>KimiCredentialsLock.HoldTree</c>, is the same primitive keyed by a sign-in folder; its Linux test, over the identity key, is built on this one once the key exists.
/// No test holds an inheritable handle in the test host (<c>NoTestHoldsAnInheritableHandleTests</c>): every inheritable take here is a <c>FakeCli hold-file</c> child.</para>
/// </remarks>
public sealed class HeldFileInheritableOnLinuxTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TempDir _dir = TempDir.For("testsToDelete-heldfile-");

    private readonly List<Process> _children = [];

    private string LockFile => _dir.At("tree.lock");

    public void Dispose()
    {
        foreach (var child in _children.Where(child => !child.HasExited))
        {
            child.Kill();
        }

        _children.ForEach(child => child.Dispose());
        _dir.Dispose();
    }

    private static string FakeCli => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo(FakeCli) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info)!;
        _children.Add(process);

        return process;
    }

    /// <summary>The holder as a child: <c>held &lt;grandchild pid&gt;</c> on stdout once it has taken the inheritable hold and started its sleeping grandchild.</summary>
    private async Task<(Process Holder, Process Grandchild)> HolderAsync()
    {
        var holder = Start("hold-file", "hold", LockFile);
        var line = await holder.StandardOutput.ReadLineAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
        if (line is null)
        {
            await holder.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Fail($"the child ended (exit {holder.ExitCode}) without taking its hold: {await holder.StandardError.ReadToEndAsync()}");
        }

        line.Should().StartWith("held ", "the child takes its hold before it says so");
        var grandchild = Process.GetProcessById(int.Parse(line[5..]));
        _children.Add(grandchild);

        return (holder, grandchild);
    }

    /// <summary>A second inheritable take, in a child of its own: <c>taken</c>, <c>busy</c> or <c>unusable</c>.</summary>
    private async Task<string> InheritableTryAsync()
    {
        var attempt = Start("hold-file", "try", LockFile);
        var answer = await attempt.StandardOutput.ReadLineAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
        await attempt.WaitForExitAsync(TestContext.Current.CancellationToken);

        return answer ?? $"nothing (exit {attempt.ExitCode}): {await attempt.StandardError.ReadToEndAsync()}";
    }

    /// <summary>Whether the file is Held at EVERY probe for 1.5 s — never one probe: rundown after a process exit takes a moment.</summary>
    private static async Task<bool> StaysHeldAsync(string path)
    {
        var until = DateTime.UtcNow.AddMilliseconds(1500);
        while (DateTime.UtcNow < until)
        {
            if (HeldFile.Probe(path) != LockProbe.Held)
            {
                return false;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return true;
    }

    [Fact]
    public async Task L_N1a_ANonInheritableTake_BesideAnInheritableHold_IsBusy()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "L-N1 asks what .NET does on Linux; the Windows share mode is the kernel's");
        await HolderAsync();

        var hold = HeldFile.Take(LockFile);
        try
        {
            hold.Should().BeOfType<Hold.Busy>("a non-inheritable take beside an inheritable hold succeeded — the hold is not a lock at all");
        }
        finally
        {
            if (hold is Hold.Taken taken)
            {
                taken.Stream.Dispose();
            }
        }
    }

    [Fact]
    public async Task L_N1a_ASecondInheritableTake_IsBusy_TheLockIsExclusive()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "L-N1 asks what .NET does on Linux; the Windows share mode is the kernel's");
        await HolderAsync();

        var answer = await InheritableTryAsync();

        answer.Should().Be("busy", "a second take of an inheritable hold succeeded — the lock is shared, not exclusive (S-K4)");
    }

    [Fact]
    public async Task L_N1b_TheHold_SurvivesItsOpenersSigkill_WhileTheGrandchildLives()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "L-N1 asks what .NET does on Linux; the Windows handle inheritance is the kernel's");
        var (holder, grandchild) = await HolderAsync();

        holder.Kill();
        await holder.WaitForExitAsync(TestContext.Current.CancellationToken);

        (await StaysHeldAsync(LockFile)).Should().BeTrue("the hold did not survive its opener's SIGKILL while the grandchild lived — the descriptor was not inherited (O_CLOEXEC) (S-K5)");

        grandchild.Kill();
        await grandchild.WaitForExitAsync(TestContext.Current.CancellationToken);

        (await Polls.Until(() => HeldFile.Probe(LockFile) == LockProbe.Free, Patience)).Should().BeTrue("the hold should be free once the last inheritor is gone");
    }
}
