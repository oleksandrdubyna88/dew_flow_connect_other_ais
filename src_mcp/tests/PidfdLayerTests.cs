using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The pidfd layer and the by-identity signalling built on it (DL9 and S-K21 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>; A3 §4.4–§4.5): the layer picks libc's wrapper or the raw
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

        calls.Log.Should().Equal($"open:{Environment.ProcessId}", $"signal:{Environment.ProcessId}:0", $"close:{Environment.ProcessId}");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheProbeProvesTheRoad_ADescriptorOfItsOwnProcess_AndSignalZeroThroughIt(bool wrapper)
    {
        var calls = new FakeCalls();

        using (PidfdLayer.Using(wrapper, calls))
        {
            PidfdLayer.Current.Should().BeOfType(wrapper ? typeof(PidfdSupport.Wrapper) : typeof(PidfdSupport.Raw));
            PidfdLayer.Refusal().Should().BeEmpty();
        }

        calls.Log.Should().Equal($"open:{Environment.ProcessId}", $"signal:{Environment.ProcessId}:0", $"close:{Environment.ProcessId}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFilterThatRefusesThePidfdOpen_ClosesTheRoad_NamingTheErrno(bool wrapper)
    {
        var calls = new FakeCalls { OpenAnswer = Eperm };

        using (PidfdLayer.Using(wrapper, calls))
        {
            PidfdLayer.Current.Should().BeOfType<PidfdSupport.Missing>("a seccomp EPERM kept the road open, and then every kill failed after Kimi was launched anyway");
            PidfdLayer.Refusal().Should().Contain("`pidfd_open` answered errno 1 (EPERM)").And.Contain("does not launch it");
            PidfdLayer.Refusal().Should().NotBe(PidfdLayer.NoPidfdSentence, "ENOSYS has its own sentence; this one names the errno it really got");
        }

        calls.Log.Should().NotContain(call => call.StartsWith("signal:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFilterThatRefusesTheSignal_ClosesTheRoad_NamingTheErrno_AndTheDescriptorIsClosed(bool wrapper)
    {
        var calls = new FakeCalls { SignalAnswer = Eperm };

        using (PidfdLayer.Using(wrapper, calls))
        {
            PidfdLayer.Current.Should().BeOfType<PidfdSupport.Missing>("a descriptor that cannot be signalled is not a road");
            PidfdLayer.Refusal().Should().Contain("`pidfd_send_signal` answered errno 1 (EPERM)").And.Contain("does not launch it");
        }

        calls.Log.Should().Equal($"open:{Environment.ProcessId}", $"signal:{Environment.ProcessId}:0", $"close:{Environment.ProcessId}");
    }

    [Fact]
    public void AnUnnamedErrno_IsStillNamedByItsNumber()
    {
        using (PidfdLayer.Using(wrapperPresent: true, new FakeCalls { OpenAnswer = 99 }))
        {
            PidfdLayer.Refusal().Should().Contain("`pidfd_open` answered errno 99");
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

    // ---------- by EXACT start ticks: one read of the stat line, no slack ----------

    private static IReadOnlyList<Signalled> KillByTicks(FakeCalls calls, FakeProcReads proc, ulong recorded) =>
        TreeSignals.Kill([new PinnedMember(Member(10), recorded)], calls, pinned => LivenessCheck.Linux(pinned.Identity, pinned.StartTicks, proc, _ => Started));

    [Fact]
    public void AMemberWhoseStartTicksMatch_IsSignalled_AndOneTickApart_IsAStrangerLeftAlone()
    {
        var proc = new FakeProcReads().With(10, "S");
        var same = new FakeCalls();
        var other = new FakeCalls();

        KillByTicks(same, proc, 4000).Should().Equal(new Signalled.Sent(Member(10)));
        var answers = KillByTicks(other, proc, 4001);

        answers.Should().ContainSingle().Which.Should().BeOfType<Signalled.Gone>().Which.Why.Should().Contain("the pid is reused");
        other.Log.Should().NotContain(call => call.StartsWith("signal:", StringComparison.Ordinal), "a pid whose start ticks differ by ONE is another process: the 1 s identity slack is not the rule here");
    }

    [Fact]
    public void APinnedMember_IsPinnedFirst_ThenChecked_ThenSignalled()
    {
        var calls = new FakeCalls();

        TreeSignals.Kill([new PinnedMember(Member(10), 4000)], calls, pinned =>
        {
            calls.Log.Add($"check:{pinned.Identity.Pid}:{pinned.StartTicks}");

            return new Liveness.Alive();
        }).Should().Equal(new Signalled.Sent(Member(10)));
        calls.Log.Should().Equal("open:10", "check:10:4000", $"signal:10:{SigKill}", "close:10");
    }

    [Fact]
    public void ThePinnedMemberOfAProcess_CarriesTheTicksOfItsOwnStatLine()
    {
        var proc = new FakeProcReads().With(10, "S");

        PinnedMember.Of(Member(10), proc).StartTicks.Should().Be(4000);
        PinnedMember.Of(Member(11), proc).StartTicks.Should().Be(0, "an unreadable stat records no ticks: the identity's start time decides");
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
            var identity = IdentityOf(process);

            var answers = TreeSignals.Kill([identity], calls);

            answers.Should().Equal(new Signalled.Sent(identity));
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
        try
        {
            fd.Should().BeGreaterThanOrEqualTo(0, "pinning this process works on any kernel from 5.3 (errno " + -fd + ")");
            calls.Signal(fd, 0).Should().Be(0, "signal 0 only asks whether the pinned process can be signalled");
            calls.Poll([fd], new bool[1], 0).Should().Be(0, "this process has not ended");
        }
        finally
        {
            if (fd >= 0)
            {
                calls.Close(fd);
            }
        }
    }

    /// <summary>
    /// The wrapper road is chosen by whether libc exports BOTH wrappers; glibc added them in 2.36. The probe is by name (works under Native AOT), so it is compared here with what libc says its version is.
    /// "libc.so.6" in the probe and "libc" in the imports are the same library on glibc.
    /// </summary>
    [Fact]
    public void TheWrapperProbe_AgreesWithGlibcsVersion_TwoThirtySixOrLater()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "glibc is a Linux library");
        var version = GlibcVersion();
        Assert.SkipWhen(version.Length == 0, "this libc is not glibc (musl has no gnu_get_libc_version): the wrappers cannot be compared with a version");

        PidfdLayer.WrapperPresent().Should().Be(Version.Parse(version) >= new Version(2, 36), $"glibc {version}: pidfd_open and pidfd_send_signal came in 2.36");
    }

    private static string GlibcVersion()
    {
        if (!NativeLibrary.TryLoad("libc.so.6", out var libc) || !NativeLibrary.TryGetExport(libc, "gnu_get_libc_version", out var export))
        {
            return string.Empty;
        }

        return Marshal.PtrToStringUTF8(Marshal.GetDelegateForFunctionPointer<GnuGetLibcVersion>(export)()) ?? string.Empty;
    }

    /// <summary><c>const char *gnu_get_libc_version(void)</c>, called through its exported address: the test project does not allow the unsafe code a <c>LibraryImport</c> needs.</summary>
    private delegate nint GnuGetLibcVersion();

    [Fact]
    public async Task ARealMember_IsEndedByItsExactStartTicks_AndAStrangersTicksLeaveItAlone()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "pidfd is a Linux call");
        using var road = PidfdLayer.Using(wrapperPresent: false);
        var process = Sleeper();
        try
        {
            var identity = IdentityOf(process);
            var member = PinnedMember.Of(identity, ProcReads.Real);
            var calls = ((PidfdSupport.Raw)PidfdLayer.Current).Calls;

            member.StartTicks.Should().BeGreaterThan(0, "the real stat line carries field 22");
            TreeSignals.Kill([member with { StartTicks = member.StartTicks + 1 }], calls).Should().ContainSingle().Which.Should().BeOfType<Signalled.Gone>();
            process.HasExited.Should().BeFalse("one tick apart is a stranger's number: it was never signalled");

            TreeSignals.Kill([member], calls).Should().Equal(new Signalled.Sent(identity));
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            process.ExitCode.Should().Be(128 + SigKill);
        }
        finally
        {
            EndAnyway(process);
        }
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
    [Theory]
    [InlineData("/bin/sh", "sleep 600 & echo $!; exec sleep 600")]
    [InlineData("/bin/bash", "sleep 600 & echo $!; sleep 2; exec sleep 600")]
    public async Task AZombie_IsGone_NotSignalled(string shell, string script)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && File.Exists(shell), "zombies and pidfd are Linux's");
        using var road = PidfdLayer.Using(wrapperPresent: false);
        using var zombie = await ZombieMaker.StartAsync(shell, script);

        await zombie.KillToZombieAsync();

        TreeSignals.Kill([zombie.Identity], ((PidfdSupport.Raw)PidfdLayer.Current).Calls).Should().Equal(new Signalled.Gone(zombie.Identity, "exited (zombie)"));
    }
}
