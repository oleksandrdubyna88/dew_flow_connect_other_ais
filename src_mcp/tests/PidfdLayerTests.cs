using System.Diagnostics;
using System.Runtime.Versioning;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The pidfd layer and the by-identity signalling built on it (DL9 and S-K21 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>; A3 §4.4–§4.5): the layer picks libc's wrapper or the raw
/// <c>syscall</c> and refuses a kernel with no pidfd, and <c>TreeSignals</c> pins → checks → signals through the descriptor, never by number. The logic runs on every platform over a fake
/// of <see cref="IPidfdCalls"/>; the REAL kernel is the Linux arms at the end (the ubuntu CI job; they skip elsewhere).
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class PidfdLayerTests
{
    private const int SigKill = 9;
    private const int Esrch = 3;
    private const int Eperm = 1;
    private const int Enosys = 38;

    private static readonly DateTime Started = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private static ProcessIdentity Member(int pid) => new(pid, Started);

    /// <summary>The calls, recorded: a descriptor is its pid; any call can be made to answer an errno; one log holds every call in order.</summary>
    private sealed class FakeCalls(List<string>? log = null) : IPidfdCalls
    {
        public List<string> Log { get; } = log ?? [];

        public int OpenAnswer { get; init; }

        public int SignalAnswer { get; init; }

        public bool Readable { get; init; }

        public int Open(int pid)
        {
            Log.Add($"open:{pid}");

            return OpenAnswer != 0 ? -OpenAnswer : pid;
        }

        public int Signal(int fd, int signal)
        {
            Log.Add($"signal:{fd}:{signal}");

            return SignalAnswer;
        }

        public int Poll(IReadOnlyList<int> fds, bool[] ready, int milliseconds)
        {
            Log.Add($"poll:{string.Join(',', fds)}");
            ready[0] = Readable;

            return Readable ? 1 : 0;
        }

        public void Close(int fd) => Log.Add($"close:{fd}");
    }

    // ---------- the layer: which road, and the refusal (S-K21) ----------

    [Fact]
    public void WithNoLibcWrapper_TheRawSyscallRoadIsTheOneResolved()
    {
        var calls = new FakeCalls();

        using (PidfdLayer.Using(wrapperPresent: false, calls))
        {
            PidfdLayer.Current.Should().BeOfType<PidfdSupport.Raw>("with no libc wrapper the layer fell back to a kill by number").Which.Calls.Should().BeSameAs(calls);
            PidfdLayer.Refusal().Should().BeEmpty();
        }

        calls.Log.Should().Equal($"open:{Environment.ProcessId}", $"close:{Environment.ProcessId}");
    }

    [Fact]
    public void WithTheLibcWrapper_TheWrapperRoadIsTheOneResolved()
    {
        var calls = new FakeCalls();

        using (PidfdLayer.Using(wrapperPresent: true, calls))
        {
            PidfdLayer.Current.Should().BeOfType<PidfdSupport.Wrapper>().Which.Calls.Should().BeSameAs(calls);
            PidfdLayer.Refusal().Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AKernelWithoutPidfd_IsRefusedWithThePlansSentence_AndNothingIsSignalled(bool wrapper)
    {
        var calls = new FakeCalls { OpenAnswer = Enosys };

        using (PidfdLayer.Using(wrapper, calls))
        {
            PidfdLayer.Current.Should().Be(new PidfdSupport.Missing(PidfdLayer.NoPidfdSentence), "a kernel without pidfd launched Kimi");
            PidfdLayer.Refusal().Should().Be(PidfdLayer.NoPidfdSentence);
        }

        calls.Log.Should().NotContain(call => call.StartsWith("signal:", StringComparison.Ordinal), "nothing is signalled by number, either");
        PidfdLayer.NoPidfdSentence.Should().Contain("`pidfd_open` answered ENOSYS").And.Contain("Linux 5.3 or later").And.Contain("does not launch it");
    }

    [Fact]
    public void AnyOtherAnswerOfTheProbe_KeepsTheRoad()
    {
        using (PidfdLayer.Using(wrapperPresent: true, new FakeCalls { OpenAnswer = Eperm }))
        {
            PidfdLayer.Current.Should().BeOfType<PidfdSupport.Wrapper>("only ENOSYS says the kernel has no pidfd; a filtered call fails per member, with its errno");
        }
    }

    [Fact]
    public void TheOverride_IsRestoredWhenItsScopeEnds()
    {
        var before = PidfdLayer.Current;

        using (PidfdLayer.Using(wrapperPresent: false, new FakeCalls { OpenAnswer = Enosys }))
        {
            PidfdLayer.Current.Should().BeOfType<PidfdSupport.Missing>();
        }

        PidfdLayer.Current.Should().BeSameAs(before);
    }

    [Fact]
    public void TheRawCallNumbers_ArePidfdOpen434_AndPidfdSendSignal424() =>
        (RawSyscallPidfdCalls.SyscallOpen, RawSyscallPidfdCalls.SyscallSendSignal).Should().Be((434, 424), "the same on x86-64 and arm64, present in every kernel from 5.3");

    // ---------- TreeSignals over the fake ----------

    private static IReadOnlyList<Signalled> Kill(FakeCalls calls, Liveness liveness, params ProcessIdentity[] members) =>
        TreeSignals.Kill(members, calls, member =>
        {
            calls.Log.Add($"check:{member.Pid}");

            return liveness;
        });

    [Fact]
    public void AnAliveMember_IsPinnedFirst_ThenChecked_ThenSignalledThroughTheDescriptor_AndTheDescriptorIsClosed()
    {
        var calls = new FakeCalls();

        var answers = Kill(calls, new Liveness.Alive(), Member(10));

        answers.Should().Equal(new Signalled.Sent(Member(10)));
        calls.Log.Should().Equal("open:10", "check:10", $"signal:10:{SigKill}", "close:10");
    }

    [Fact]
    public void OnlySigkillIsSent()
    {
        var calls = new FakeCalls();

        Kill(calls, new Liveness.Alive(), Member(10), Member(20));

        calls.Log.Where(call => call.StartsWith("signal:", StringComparison.Ordinal)).Should().Equal($"signal:10:{SigKill}", $"signal:20:{SigKill}");
    }

    [Fact]
    public void AGoneMember_IsNotSignalled_AndItsReasonIsKept()
    {
        var calls = new FakeCalls();

        var answers = Kill(calls, new Liveness.Gone("the pid is reused — started at later"), Member(10));

        answers.Should().Equal(new Signalled.Gone(Member(10), "the pid is reused — started at later"));
        calls.Log.Should().Equal("open:10", "check:10", "close:10");
    }

    [Fact]
    public void AMemberThatCannotBeChecked_IsNotSignalled_UnlessItsDescriptorSaysItEnded()
    {
        var alive = new FakeCalls();
        var ended = new FakeCalls { Readable = true };

        var unknown = Kill(alive, new Liveness.Unknown("UnauthorizedAccessException reading /proc/10/stat"), Member(10));
        var gone = Kill(ended, new Liveness.Unknown("UnauthorizedAccessException reading /proc/10/stat"), Member(10));

        unknown.Should().Equal(new Signalled.Unknown(Member(10), "UnauthorizedAccessException reading /proc/10/stat"));
        alive.Log.Should().NotContain(call => call.StartsWith("signal:", StringComparison.Ordinal), "a process that cannot be told from another is not signalled");
        gone.Should().ContainSingle().Which.Should().BeOfType<Signalled.Gone>().Which.Why.Should().Contain("pidfd is readable");
        ended.Log.Should().NotContain(call => call.StartsWith("signal:", StringComparison.Ordinal));
    }

    [Fact]
    public void APidNobodyHolds_IsGone_AndNoDescriptorWasMade()
    {
        var calls = new FakeCalls { OpenAnswer = Esrch };

        Kill(calls, new Liveness.Alive(), Member(10)).Should().Equal(new Signalled.Gone(Member(10), "no such process"));
        calls.Log.Should().Equal("open:10");
    }

    [Fact]
    public void APidThatCannotBePinned_IsAFailureNamingItsErrno_NeverAKillByNumber()
    {
        var calls = new FakeCalls { OpenAnswer = Eperm };

        Kill(calls, new Liveness.Alive(), Member(10)).Should().Equal(new Signalled.Failed(Member(10), Eperm));
        calls.Log.Should().Equal("open:10");
    }

    [Fact]
    public void ASignalToAProcessThatEndedInBetween_IsGone_AndOneTheKernelRefuses_IsFailed()
    {
        Kill(new FakeCalls { SignalAnswer = Esrch }, new Liveness.Alive(), Member(10)).Should().Equal(new Signalled.Gone(Member(10), "no such process"));
        Kill(new FakeCalls { SignalAnswer = Eperm }, new Liveness.Alive(), Member(10)).Should().Equal(new Signalled.Failed(Member(10), Eperm));
    }

    [Fact]
    public void AnswersComeBackInTheOrderOfTheMembers_OnePinEach()
    {
        var calls = new FakeCalls();

        var answers = TreeSignals.Kill([Member(30), Member(20)], calls, member => member.Pid == 30 ? new Liveness.Gone("exited") : new Liveness.Alive());

        answers.Select(answer => answer.GetType()).Should().Equal(typeof(Signalled.Gone), typeof(Signalled.Sent));
        calls.Log.Where(call => call.StartsWith("close:", StringComparison.Ordinal)).Should().Equal("close:30", "close:20");
    }

    // ---------- the real kernel (Linux CI) ----------

    private static Process Sleeper()
    {
        var info = new ProcessStartInfo("sleep") { UseShellExecute = false, ArgumentList = { "600" } };

        return Process.Start(info)!;
    }

    private static void EndAnyway(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.Dispose();
    }

    private static ProcessIdentity IdentityOf(Process process) => new(process.Id, ProcessTracking.StartedAt(process.Id)!.Value);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EitherRoad_PinsAndEndsARealProcessByIdentity(bool wrapper)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "pidfd is a Linux call");
        Assert.SkipWhen(wrapper && PidfdLayer.Current is not PidfdSupport.Wrapper, "this libc has no pidfd wrappers (glibc before 2.36): the raw road is the only one, and it is the other row");
        using var road = PidfdLayer.Using(wrapper);
        var calls = PidfdLayer.Current switch
        {
            PidfdSupport.Wrapper w => w.Calls,
            PidfdSupport.Raw r => r.Calls,
            _ => throw new InvalidOperationException("this kernel has no pidfd: Kimi would be refused, and a CI kernel is 5.3 or later"),
        };
        var process = Sleeper();
        try
        {
            var answers = TreeSignals.Kill([IdentityOf(process)], calls);

            answers.Should().Equal(new Signalled.Sent(IdentityOf(process)));
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            process.ExitCode.Should().Be(128 + SigKill, "it was ended by SIGKILL, through the descriptor");
        }
        finally
        {
            EndAnyway(process);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EitherRoad_AnswersEsrchForANumberNobodyHolds(bool wrapper)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "pidfd is a Linux call");
        Assert.SkipWhen(wrapper && PidfdLayer.Current is not PidfdSupport.Wrapper, "this libc has no pidfd wrappers (glibc before 2.36)");
        IPidfdCalls calls = wrapper ? new LibcPidfdCalls() : new RawSyscallPidfdCalls();

        calls.Open(int.MaxValue).Should().Be(-Esrch, "no process has that number: -ESRCH, never a descriptor");

        var fd = calls.Open(Environment.ProcessId);
        fd.Should().BeGreaterThanOrEqualTo(0, "pinning this process works on any kernel from 5.3 (errno " + -fd + ")");
        calls.Signal(fd, 0).Should().Be(0, "signal 0 only asks whether the pinned process can be signalled");
        calls.Poll([fd], new bool[1], 0).Should().Be(0, "this process has not ended");
        calls.Close(fd);
    }

    [Fact]
    public void APidWhoseStartDiffers_IsLeftAlone()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "pidfd is a Linux call");
        using var road = PidfdLayer.Using(wrapperPresent: false);
        var process = Sleeper();
        try
        {
            var recorded = new ProcessIdentity(process.Id, ProcessTracking.StartedAt(process.Id)!.Value - TimeSpan.FromHours(1));

            var answers = TreeSignals.Kill([recorded], ((PidfdSupport.Raw)PidfdLayer.Current).Calls);

            answers.Should().ContainSingle().Which.Should().BeOfType<Signalled.Gone>().Which.Why.Should().Contain("the pid is reused");
            process.HasExited.Should().BeFalse("the number is a stranger's now: it was never signalled");
        }
        finally
        {
            EndAnyway(process);
        }
    }

    /// <summary>S-K22's half this layer owns: an exited process its parent has not reaped — a zombie — is Gone, never a survivor.</summary>
    [Fact]
    public async Task AZombie_IsGone_NotSignalled()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "zombies and pidfd are Linux's");
        using var road = PidfdLayer.Using(wrapperPresent: false);
        var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardOutput = true, ArgumentList = { "-c", "sleep 600 & echo $!; exec sleep 600" } };
        using var parent = Process.Start(info)!;
        try
        {
            var pid = int.Parse((await parent.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken))!.Trim());
            var identity = new ProcessIdentity(pid, ProcessTracking.StartedAt(pid)!.Value);
            using (var child = Process.GetProcessById(pid))
            {
                child.Kill();
            }

            await BecomesZombieAsync(pid);

            TreeSignals.Kill([identity], ((PidfdSupport.Raw)PidfdLayer.Current).Calls).Should().Equal(new Signalled.Gone(identity, "exited (zombie)"));
        }
        finally
        {
            EndAnyway(parent);
        }
    }

    private static async Task BecomesZombieAsync(int pid)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            if (ProcStat.ReadFor(pid, ProcReads.Real) is ProcStatRead.Read { Line.State: "Z" })
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail("the outer sleep never reaps, so the killed child should have stayed a zombie");
    }
}
