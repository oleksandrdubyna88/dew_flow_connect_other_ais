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

        File.WriteAllText(Path.Combine(RunTemp.Folder, Owned.NameFor("orphan") + ".owner"), Owner.This.Format() + "\n");
    }
}
