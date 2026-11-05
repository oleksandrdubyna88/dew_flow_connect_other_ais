using System.ComponentModel;
using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// How a sweeper judges a run folder's owner claim: only on the SAME host and operating system, and a refused process lookup is "alive"
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, D2 and the risk consult for epic 2; the rule the sibling repositories' own
/// liveness checks follow — <c>WorkerLiveness.ProcessIsAlive</c> in the benchmark, <c>WorkerIdentity.IsProvablyGoneOn</c> in the RAG repository).
/// </summary>
/// <remarks>
/// <para><b>1. Another host or another operating system is never "dead".</b> A pid means something only on the machine that wrote it, so a claim
/// from elsewhere (a shared temp folder, a copied one) is left alone — a leak, never a deletion — and the process lookup is not even asked.</para>
/// <para><b>2. Only "no such process" and "it ended" mean gone.</b> <see cref="ArgumentException"/> (no such pid) and
/// <see cref="InvalidOperationException"/> (it ended between the lookup and the question) are dead; <see cref="Win32Exception"/> (access denied, a
/// protected process) is ALIVE: being refused an answer is not being told the process ended. Node's twin: only <c>ESRCH</c> is dead, <c>EPERM</c> is alive.</para>
/// </remarks>
public sealed class OwnerClaimTests
{
    private const string ThisHost = "BUILD-01";

    private static ClaimVerdict Judge(string claim, string host, string os, bool alive) =>
        OwnerClaim.Judge(claim, host, os, _ => alive);

    [Theory]
    [InlineData("BUILD-01@linux:4242", "BUILD-01", "linux", false, ClaimVerdict.Leftover)]
    [InlineData("BUILD-01@linux:4242:1000", "BUILD-01", "linux", false, ClaimVerdict.Leftover)]
    [InlineData("BUILD-01@linux:4242", "BUILD-01", "linux", true, ClaimVerdict.Keep)]
    [InlineData("build-01@linux:4242", "BUILD-01", "linux", false, ClaimVerdict.Leftover)]
    [InlineData("OTHER-02@linux:4242", "BUILD-01", "linux", false, ClaimVerdict.Keep)]
    [InlineData("BUILD-01@win32:4242", "BUILD-01", "linux", false, ClaimVerdict.Keep)]
    [InlineData("BUILD-01@darwin:4242", "BUILD-01", "win32", false, ClaimVerdict.Keep)]
    [InlineData("not a claim", "BUILD-01", "linux", false, ClaimVerdict.Keep)]
    public void ADeadOwner_IsTakenOnlyOnTheSameHostAndOs(string claim, string host, string os, bool alive, ClaimVerdict expected) =>
        Judge(claim, host, os, alive).Should().Be(expected);

    [Fact]
    public void ThePidOfAnotherHostsClaim_IsNeverLookedUp()
    {
        var asked = new List<int>();

        var verdict = OwnerClaim.Judge("OTHER-02@linux:4242", ThisHost, "linux", pid => { asked.Add(pid); return false; });

        verdict.Should().Be(ClaimVerdict.Keep);
        asked.Should().BeEmpty("a pid means something only on the machine that wrote it");
    }

    [Fact]
    public void ThePidOfThisHostsClaim_IsLookedUpOnce_WithTheClaimsPid()
    {
        var asked = new List<int>();

        OwnerClaim.Judge("BUILD-01@win32:4242", ThisHost, "win32", pid => { asked.Add(pid); return false; });

        asked.Should().Equal(4242);
    }

    public static TheoryData<string, bool> Lookups() => new()
    {
        { nameof(ArgumentException), false },
        { nameof(InvalidOperationException), false },
        { nameof(Win32Exception), true },
        { "returns", true },
    };

    [Theory]
    [MemberData(nameof(Lookups))]
    public void AProcessLookup_ThatSaysNoSuchProcessOrEnded_IsDead_AndOneThatIsRefusedIsAlive(string outcome, bool alive)
    {
        void Probe(int pid) => _ = outcome switch
        {
            nameof(ArgumentException) => throw new ArgumentException("no such process"),
            nameof(InvalidOperationException) => throw new InvalidOperationException("it ended"),
            nameof(Win32Exception) => throw new Win32Exception(5, "access denied"),
            _ => pid,
        };

        OwnerClaim.ProcessIsAlive(4242, Probe).Should().Be(alive, "only 'no such process' and 'it ended' mean gone; being refused is not an answer");
    }

    [Fact]
    public void AnUnexpectedLookupFailure_IsNotSwallowed()
    {
        var act = () => OwnerClaim.ProcessIsAlive(4242, _ => throw new IOException("something else"));

        act.Should().Throw<IOException>("a failure nobody classified is not evidence of death — and not of life either");
    }

    [Fact]
    public void TheRealLookup_SeesThisProcessAlive_AndAChildThatEndedGone()
    {
        OwnerClaim.ProcessIsAlive(Environment.ProcessId).Should().BeTrue();

        using var child = Process.Start(OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true }
            : new ProcessStartInfo("sh", "-c true"))!;
        var pid = child.Id;
        child.WaitForExit();

        OwnerClaim.ProcessIsAlive(pid).Should().BeFalse("the child ended and nothing reused its pid within milliseconds");
    }
}
