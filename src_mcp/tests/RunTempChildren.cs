using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The names the gate's child runs look for (E5 S3). Each class is meant ONLY for a child that <see cref="SuiteChild"/> starts with its variable set; in a
/// normal run each skips, naming why. A child whose gate works never reaches any of them.
/// </summary>
public static class RunTempChildren
{
    /// <summary>The file a sentinel appends one line to — set only by a scenario that asks "did discovery or a test body run".</summary>
    public const string SentinelVariable = "COAI_S3_SENTINEL";

    /// <summary>Set to 1 by the scenario that kills a child on purpose.</summary>
    public const string KilledVariable = "COAI_S3_KILLED_CHILD";

    internal static void Append(string what)
    {
        if (Environment.GetEnvironmentVariable(SentinelVariable) is { Length: > 0 } file)
        {
            File.AppendAllText(file, what + "\n");
        }
    }
}

/// <summary>A theory whose <c>MemberData</c> runs at DISCOVERY: with the sentinel variable set it appends one line, so a refused child proves discovery never ran.</summary>
public sealed class DiscoverySentinel
{
    public static TheoryData<int> Rows()
    {
        RunTempChildren.Append("discovery");

        return new TheoryData<int> { 1 };
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void ReadsTheSentinel_OnlyWhenAChildIsAskedTo(int row)
    {
        if (Environment.GetEnvironmentVariable(RunTempChildren.SentinelVariable) is not { Length: > 0 })
        {
            Assert.Skip("child-only: a scenario of RunTempTests starts the suite on this class with COAI_S3_SENTINEL set");
        }

        RunTempChildren.Append("test body");
        row.Should().Be(1);
    }
}

/// <summary>A fact that appends to the sentinel when it runs: the fallback's proof that no test body ran when the gate was bypassed.</summary>
public sealed class GateBypassedSentinel
{
    [Fact]
    public void RunsOnlyWhenTheGateWasBypassed()
    {
        if (Environment.GetEnvironmentVariable(RunTempChildren.SentinelVariable) is not { Length: > 0 })
        {
            Assert.Skip("child-only: a scenario of RunTempGateTests starts the suite on this class with COAI_S3_SENTINEL set");
        }

        RunTempChildren.Append("test body");
    }
}

/// <summary>A fact that ends its process on purpose, leaving the run folder and its claim: the killed run the sweep must collect.</summary>
public sealed class KilledRunChild
{
    [Fact]
    public void EndsTheProcessWithoutCleanup()
    {
        if (Environment.GetEnvironmentVariable(RunTempChildren.KilledVariable) != "1")
        {
            Assert.Skip("child-only: AKilledRun_IsCollected_ByTheSweep starts the suite on this class with COAI_S3_KILLED_CHILD=1");
        }

        Environment.FailFast("E5 S3: killed on purpose");
    }
}

/// <summary>A fact that plants an ORPHAN CLAIM inside its own run folder (a claim whose folder is missing), absent from the registry: the end-of-run audit must name it before the run folder ends (E5 S6, D8 (o)).</summary>
public sealed class OrphanClaimChild
{
    [Fact]
    public void PlantsAClaimWhoseFolderIsMissing()
    {
        if (Environment.GetEnvironmentVariable("COAI_S6_ORPHAN_CLAIM") != "1")
        {
            Assert.Skip("child-only: AnOrphanClaim_IsReportedByTheAudit_BeforeTheRunFolderEnds starts the suite on this class with COAI_S6_ORPHAN_CLAIM=1");
        }

        var claim = Path.Combine(RunTemp.Folder, Owned.NameFor("orphan") + ".owner");
        File.WriteAllText(claim, Owner.This.Format() + "\n");

        // An elevated Windows process makes files Administrators own, and the audit reads those as a stranger's (F31). Re-own this one, so the plant tests the ORDER.
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("COAI_S6_ORPHAN_CLAIM_UNTOUCHED") != "1")
        {
            using var windows = new WindowsOwnership();
            windows.OwnedByCaller(claim);
            if (!Scope.IsMine(claim))
            {
                throw new InvalidOperationException("the plant is not the caller's after re-owning: the profile key does not hold for " + claim);
            }
        }
    }
}

/// <summary>
/// Two children of ONE run (E5 S6, must-not-copy #1): both are given the same live <c>TESTSTODELETE_RUN</c> record and one private root. Role <c>b</c> makes its owned folder and WAITS for a go file; role <c>a</c> makes one,
/// disposes it and ends, so ITS end-of-run audit runs while b's folder is still there. Skips unless <c>LeftoversCoaiLayoutTests</c> starts it with the variables set.
/// </summary>
public sealed class SiblingChild
{
    [Fact]
    public void MakesItsFolder_AndEnds()
    {
        var role = Environment.GetEnvironmentVariable("COAI_S6_SIBLING");
        if (string.IsNullOrEmpty(role))
        {
            Assert.Skip("child-only: TwoChildSuitesUnderOneRun_TheFirstToFinish_LeavesTheSecondsFolder starts the suite on this class with COAI_S6_SIBLING set");
        }

        var shared = Environment.GetEnvironmentVariable("COAI_S6_SHARED_ROOT")!;
        var dir = OwnedDir.Create("sibling-" + role, shared);
        try
        {
            if (role == "b")
            {
                // Written under another name and moved, so the parent never sees the file before it holds the path (a slow, instrumented run did).
                var ready = Environment.GetEnvironmentVariable("COAI_S6_READY")!;
                File.WriteAllText(ready + ".part", dir.Path);
                File.Move(ready + ".part", ready);
                var go = Environment.GetEnvironmentVariable("COAI_S6_GO")!;
                var until = DateTime.UtcNow.AddSeconds(120); // longer than child a's limit, and the parent's finally always writes the go file
                while (!File.Exists(go) && DateTime.UtcNow < until)
                {
                    Thread.Sleep(50);
                }
            }
        }
        finally
        {
            dir.Dispose();
        }
    }
}

/// <summary>Writes whether the repository's baseline file is found from the repository root as this child sees it (E5 S6, R12): the working folder of the child is the test's choice.</summary>
public sealed class BaselineLocationChild
{
    [Fact]
    public void WritesWhetherTheBaselineIsFound()
    {
        var outFile = Environment.GetEnvironmentVariable("COAI_S6_BASELINE_OUT");
        Assert.SkipWhen(string.IsNullOrEmpty(outFile), "child-only: TheBaselineIsFound_FromBothPlaces starts the suite on this class with COAI_S6_BASELINE_OUT set");

        File.WriteAllText(outFile!, File.Exists(Path.Combine(RepositoryRoot.Path, "raw-temp-baseline.json")).ToString());
    }
}
