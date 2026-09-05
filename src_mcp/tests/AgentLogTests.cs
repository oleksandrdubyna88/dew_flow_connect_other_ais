using System.Text.Json;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the agent was doing between one gate and the next, taken from its own transcript.
/// </summary>
/// <remarks>
/// The operator's reasoning, 2026-09-05: the session opened at 13:00 and the plan review ran at
/// 13:39, so everything in that stretch belongs to the plan round; the code was written between
/// 13:39 and 15:03, so that stretch belongs to the code round. The gate already records what other
/// models found. Attaching what the agent was doing while they found it is the other half of a
/// blind-spot analysis.
/// </remarks>
public sealed class AgentLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-log-" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly DateTime Opened = DateTime.Parse("2026-09-05T13:00:00Z").ToUniversalTime();
    private static readonly DateTime PlanRound = DateTime.Parse("2026-09-05T13:39:00Z").ToUniversalTime();
    private static readonly DateTime CodeRound = DateTime.Parse("2026-09-05T15:03:00Z").ToUniversalTime();

    private string Transcript(string name, params string[] lines)
    {
        var folder = Path.Combine(_dir, name);
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "session.jsonl");
        File.WriteAllLines(file, lines);

        return file;
    }

    private static string Line(string utc, string type, string cwd, string said) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["timestamp"] = utc,
            ["type"] = type,
            ["cwd"] = cwd,
            ["message"] = new Dictionary<string, object>
            {
                ["content"] = new object[] { new Dictionary<string, string> { ["type"] = "text", ["text"] = said } },
            },
        });

    [Fact]
    public void OnlyWhatHappenedInsideTheWindowIsKept()
    {
        Transcript("d--rsd-repo",
            Line("2026-09-05T12:59:00Z", "user", "D:/repo", "before the session even opened"),
            Line("2026-09-05T13:20:00Z", "assistant", "D:/repo", "reading the plan"),
            Line("2026-09-05T13:38:00Z", "assistant", "D:/repo", "about to call the gate"),
            Line("2026-09-05T14:10:00Z", "assistant", "D:/repo", "that is the next round's work"));

        var slice = AgentLog.Slice(_dir, Opened, PlanRound, "D:/repo");

        slice.Should().Contain("reading the plan").And.Contain("about to call the gate");
        slice.Should().NotContain("before the session even opened");
        slice.Should().NotContain("that is the next round");
    }

    [Fact]
    public void TheSecondWindowGetsTheImplementationStretch()
    {
        Transcript("d--rsd-repo",
            Line("2026-09-05T13:20:00Z", "assistant", "D:/repo", "still planning"),
            Line("2026-09-05T14:10:00Z", "assistant", "D:/repo", "writing the code"),
            Line("2026-09-05T15:01:00Z", "assistant", "D:/repo", "running the tests"));

        var slice = AgentLog.Slice(_dir, PlanRound, CodeRound, "D:/repo");

        slice.Should().Contain("writing the code").And.Contain("running the tests");
        slice.Should().NotContain("still planning");
    }

    [Fact]
    public void WhenAnAgentIsStandingInTheRepositoryUnderReview_OnlyItsOwnWorkIsKept()
    {
        // Several agents work on this machine at once; the one in this repository is the one that
        // called this gate.
        Transcript("d--rsd-repo", Line("2026-09-05T13:20:00Z", "assistant", "D:/repo", "the work under review"));
        Transcript("d--rsd-other", Line("2026-09-05T13:21:00Z", "assistant", "D:/other", "somebody else entirely"));

        var slice = AgentLog.Slice(_dir, Opened, PlanRound, "D:/repo");

        slice.Should().Contain("the work under review");
        slice.Should().NotContain("somebody else entirely");
    }

    [Fact]
    public void AnAgentInASubdirectoryIsStillInTheRepository()
    {
        // It answered no and swept the whole machine instead. Raised by the gate's security
        // reviewers, 2026-09-05.
        Transcript("d--rsd-repo-src", Line("2026-09-05T13:20:00Z", "assistant", "D:/repo/src_vs_code", "working in a subfolder"));
        Transcript("d--rsd-other", Line("2026-09-05T13:21:00Z", "assistant", "D:/repo-two", "a different repository whose name starts the same"));

        var slice = AgentLog.Slice(_dir, Opened, PlanRound, "D:/repo");

        slice.Should().Contain("working in a subfolder");
        slice.Should().NotContain("a different repository");
    }

    [Fact]
    public void WhenNobodyWasStandingThere_ONESessionIsTaken_NotEveryProjectOnTheMachine()
    {
        // The ordinary case is an agent rooted in another folder driving this gate, so taking
        // nothing would lose the record. Taking EVERYTHING would copy other projects' transcripts —
        // and whatever they contain — into this repository's database, which is what the gate's
        // security reviewers objected to. The busiest transcript in the window is the one that was
        // doing the work.
        Transcript("d--rsd-elsewhere",
            Line("2026-09-05T13:20:00Z", "assistant", "D:/elsewhere", "the actual caller"),
            Line("2026-09-05T13:21:00Z", "assistant", "D:/elsewhere", "still the actual caller"));
        Transcript("d--rsd-unrelated", Line("2026-09-05T13:22:00Z", "assistant", "D:/unrelated", "somebody else's afternoon"));

        var slice = AgentLog.Slice(_dir, Opened, PlanRound, "D:/repo");

        slice.Should().Contain("the actual caller");
        slice.Should().NotContain("somebody else");
    }

    [Fact]
    public void OnlyTheDaysTheWindowTouchesAreEvenLookedAt()
    {
        // A transcript is tens of megabytes and a window is usually one afternoon of it, so a line
        // that does not mention a day in range is skipped before it is parsed.
        AgentLog.Days(Opened, PlanRound).Should().Equal(["2026-09-05"]);
        AgentLog.Days(Opened, Opened.AddDays(2)).Should().HaveCount(3);
        AgentLog.Days(Opened, Opened.AddDays(30)).Should().BeEmpty("a window that wide is not worth listing, so every line is read");
    }

    [Fact]
    public void APathWrittenTheOtherWayRoundIsTheSamePath()
    {
        Transcript("d--rsd-repo", Line("2026-09-05T13:20:00Z", "assistant", @"D:\repo\", "backslashes and a trailing one"));

        AgentLog.Slice(_dir, Opened, PlanRound, "D:/repo").Should().Contain("backslashes");
    }

    [Fact]
    public void AToolCallKeepsItsNameRatherThanItsArguments()
    {
        var withTool = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["timestamp"] = "2026-09-05T13:20:00Z",
            ["type"] = "assistant",
            ["cwd"] = "D:/repo",
            ["message"] = new Dictionary<string, object>
            {
                ["content"] = new object[]
                {
                    new Dictionary<string, string> { ["type"] = "text", ["text"] = "editing" },
                    new Dictionary<string, object>
                    {
                        ["type"] = "tool_use",
                        ["name"] = "Edit",
                        ["input"] = new Dictionary<string, string> { ["file_path"] = "a very long path nobody needs here" },
                    },
                },
            },
        });
        Transcript("d--rsd-repo", withTool);

        var slice = AgentLog.Slice(_dir, Opened, PlanRound, "D:/repo");

        slice.Should().Contain("[Edit]");
        slice.Should().NotContain("a very long path nobody needs here");
    }

    [Fact]
    public void AHalfWrittenLastLineIsSkipped_NotAFailure()
    {
        // The CLI is appending to this file while it is read; the last line is regularly incomplete.
        Transcript("d--rsd-repo",
            Line("2026-09-05T13:20:00Z", "assistant", "D:/repo", "a whole line"),
            "{\"timestamp\":\"2026-09-05T13:21:00Z\",\"type\":\"assis");

        AgentLog.Slice(_dir, Opened, PlanRound, "D:/repo").Should().Contain("a whole line");
    }

    [Fact]
    public void ASliceThatHadToBeCutSaysSo()
    {
        // A truncated record that looks complete is worse than no record: somebody counts the
        // entries later and reads the cap as the measurement.
        var many = Enumerable.Range(0, 500)
            .Select(n => Line("2026-09-05T13:2" + (n % 10) + ":00Z", "assistant", "D:/repo", "entry " + n))
            .ToArray();
        Transcript("d--rsd-repo", many);

        var slice = AgentLog.Slice(_dir, Opened, PlanRound, "D:/repo");

        slice.Should().Contain("truncated").And.Contain("further entries were not kept");
        JsonDocument.Parse(slice).RootElement.GetArrayLength().Should().BeLessThan(500);
    }

    [Fact]
    public void NoTranscriptsAtAllIsAnEmptySlice_NotAnError()
    {
        AgentLog.Slice(Path.Combine(_dir, "nothing here"), Opened, PlanRound, "D:/repo").Should().BeEmpty();
    }

    [Fact]
    public void AWindowThatEndsBeforeItStartsIsEmpty()
    {
        Transcript("d--rsd-repo", Line("2026-09-05T13:20:00Z", "assistant", "D:/repo", "anything"));

        AgentLog.Slice(_dir, PlanRound, Opened, "D:/repo").Should().BeEmpty();
    }

    [Fact]
    public void TheSliceIsValidJsonWithTheInstantAndTheKindOfEachEntry()
    {
        Transcript("d--rsd-repo", Line("2026-09-05T13:20:00Z", "assistant", "D:/repo", "said something"));

        var first = JsonDocument.Parse(AgentLog.Slice(_dir, Opened, PlanRound, "D:/repo")).RootElement[0];

        first.GetProperty("kind").GetString().Should().Be("assistant");
        first.GetProperty("said").GetString().Should().Be("said something");
        first.GetProperty("utc").GetString().Should().StartWith("2026-09-05T13:20:00");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not a failing test.
        }
    }
}
