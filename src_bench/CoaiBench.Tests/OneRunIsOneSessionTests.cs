using Xunit;
using FluentAssertions;
using CoaiBench.Cli;
using CoaiBench.Model;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// A campaign is many runs; the machine underneath is one, and it is somebody's.
/// </summary>
/// <remarks>
/// <para>Both of these were found by the campaign of 2026-09-04 rather than by anybody reading the
/// code, and both come from the same decision: the bench runs against the REAL data directory,
/// because the operator asked to watch the rounds appear in the panel while it works. That directory
/// belongs to every window on this machine.</para>
/// </remarks>
public sealed class OneRunIsOneSessionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-campaign-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly VendorConfig[] Configured =
    [
        new("codex", "codex", "gpt-5.6-luna"),
        new("local", "local", "Qwen3.5-35B-A3B-Q5_vk128:latest"),
    ];

    private static Bench BenchOf(params Case[] corpus) =>
        new(new Options { Arms = ["codex"], Repeat = 2, Repo = "D:/repo" },
            corpus,
            Configured,
            new Dictionary<string, string>(StringComparer.Ordinal),
            _ => { });

    private void Session(string name, string repo, string branch, string body)
    {
        var sessions = Path.Combine(_dir, "sessions");
        Directory.CreateDirectory(sessions);
        File.WriteAllText(
            Path.Combine(sessions, $"session-{name}.json"),
            $$"""{ "state": { "repoPath": "{{repo}}", "branch": "{{branch}}" }, {{body}} }""");
    }

    [Fact]
    public void TheDiskThisRunIsJudGedOn_IsThisRunsSession()
    {
        // Reported as `NOT RESOLVABLE: 1 still running, 40 pending` against a run that had in fact
        // finished cleanly. The forty were everybody's, and so was the one: a neighbour's window was
        // mid-round in the same directory. A run answers for its own session and no other.
        Session("mine", "D:/repo", "abc123", """ "rounds": [ { "status": "done" } ], "pending": [ { "t": 1 } ] """);
        Session("theirs", "D:/other", "main", """ "rounds": [ { "status": "running" } ], "pending": [] """);

        var read = OnDisk.Read(_dir, "D:/repo", "abc123");

        read.Clean.Should().BeTrue("the neighbour's unfinished round is not this run's business");
        read.Pending.Should().Be(1, "and neither are the neighbour's findings");
        read.StillRunning.Should().Be(0);
    }

    [Fact]
    public void ASessionThatIsNotThere_SaysSoRatherThanReadingSomebodyElses()
    {
        Session("theirs", "D:/other", "main", """ "rounds": [ { "status": "done" } ], "pending": [ { "t": 1 } ] """);

        var read = OnDisk.Read(_dir, "D:/repo", "abc123");

        read.Clean.Should().BeFalse();
        read.Note.Should().Contain("no session");
    }

    [Fact]
    public void EveryRunIsADifferentCALLER()
    {
        // The split order is given ONCE per calling AI session, which is what stops epics of epics.
        // A campaign that calls with one identity therefore measures the split path exactly once and
        // the already-split path every time after — and three of four runs came back reading
        // `SETTINGS NOT APPLIED: COAI_SPLIT_WITH_FABLE is on` while the setting was working
        // perfectly. Each run models one AI session, so each run IS one.
        var bench = BenchOf(new Case("plan-a", "todo/PLAN_a.md"));
        var cells = bench.Cells();

        var callers = cells
            .Select(c => bench.EnvironmentFor(c)["COAI_CALLER_SESSION"])
            .ToList();

        callers.Should().OnlyHaveUniqueItems("two runs are two sessions, not one session run twice");
        callers.Should().AllSatisfy(c => c.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void ACallerIdentityIsStableForTheRunItNames()
    {
        // Asked twice about the same cell it must give the same answer: the identity is part of what
        // the run IS, and a resumed campaign must not re-order what an interrupted one claimed.
        var bench = BenchOf(new Case("plan-a", "todo/PLAN_a.md"));
        var cell = bench.Cells()[0];

        bench.EnvironmentFor(cell)["COAI_CALLER_SESSION"]
            .Should().Be(bench.EnvironmentFor(cell)["COAI_CALLER_SESSION"]);
    }
}
