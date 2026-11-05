using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S7 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c> (D4, E3): a real code round removes the two folders it always makes empty — the
/// repair launch's <c>coai-repair-</c> and, in the <c>none</c> workspace mode, the review launch's <c>coai-noworkspace-</c> — when it ends. NON-recursive,
/// only when EMPTY; a folder that is not empty, that cannot be removed or that a surviving descendant still has as its working folder is said once
/// and left to the six-hour sweep, and the round's outcome is the same in every arm.
/// </summary>
[Collection("fakecli-env")]
public sealed class TheRoundsEmptyFoldersAreRemovedTests : IAsyncLifetime
{
    private readonly CodeRoundHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private const string SweepSentence = "six-hour";

    /// <summary>The folders the round's launches were GIVEN, read from the launches themselves — never from a name pattern over the shared temp.</summary>
    private sealed record RoundFolders(string Repair, string Workspace)
    {
        public static RoundFolders Of(CodeRoundHarness.RepairedRound round) => new(OnlyOne(round.Repairs, "coai-repair-"), OnlyOne(round.Reviews, "coai-noworkspace-"));

        private static string OnlyOne(IReadOnlyList<CodeRoundHarness.RepairLaunch> launches, string prefix)
        {
            var folders = launches.Select(launch => launch.WorkingDirectory).Distinct().ToList();
            folders.Should().ContainSingle("one round makes one such folder, and its launches share it").Which
                .Should().Match($"*{Path.DirectorySeparatorChar}{prefix}*");

            return folders[0];
        }
    }

    private static string Verdict(string answer)
    {
        using var parsed = JsonDocument.Parse(answer);
        parsed.RootElement.TryGetProperty("error", out var error).Should().BeFalse($"the round ended with an answer, not an error: {answer}");

        return parsed.RootElement.GetProperty("verdict").GetString() ?? string.Empty;
    }

    private static IReadOnlyList<string> WarningsAbout(ListSink sink, string folder) =>
        [.. sink.Lines.Where(line => line.Contains(folder, StringComparison.Ordinal))];

    private static Serilog.ILogger LogInto(ListSink sink) => new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();

    [Fact]
    public async Task ARoundRemovesItsEmptyRepairAndWorkspaceFolders()
    {
        var sink = new ListSink();

        var round = await _h.RepairedCodeRoundAsync("none", log: LogInto(sink));

        var folders = RoundFolders.Of(round);
        Directory.Exists(folders.Repair).Should().BeFalse("the round made its repair folder empty and must remove it when it ends");
        Directory.Exists(folders.Workspace).Should().BeFalse("the round made its workspace folder empty and must remove it when it ends");
        Verdict(round.Answer).Should().Be("proceed");
        WarningsAbout(sink, folders.Repair).Should().BeEmpty("a removal that worked says nothing");
    }

    [Fact]
    public async Task AFolderThatIsNotEmpty_IsKeptAndWarnedAbout_AndTheRoundStillEnds()
    {
        var sink = new ListSink();
        var planted = new System.Collections.Concurrent.ConcurrentBag<string>();

        var round = await _h.RepairedCodeRoundAsync("none", log: LogInto(sink), beforeARepair: folder =>
        {
            var file = Path.Combine(folder, $"{Guid.NewGuid():N}.txt");
            File.WriteAllText(file, "a descendant wrote this");
            planted.Add(file);
        });

        Verdict(round.Answer).Should().Be("proceed", "the same outcome as a round whose folders were empty");
        var folders = RoundFolders.Of(round);
        planted.Should().NotBeEmpty("a repair was launched, so the file was planted while the round was still running");
        planted.Should().OnlyContain(file => File.Exists(file), "the removal is not recursive: a folder with a file in it is kept whole");
        Directory.Exists(folders.Repair).Should().BeTrue();
        WarningsAbout(sink, folders.Repair).Should().ContainSingle("one warning, naming the folder").Which.Should().Contain(SweepSentence);
        Directory.Exists(folders.Workspace).Should().BeFalse("the other folder was empty, so it went");
    }

    [Fact]
    public async Task AFolderThatCannotBeRemoved_IsLeftToTheSweep_AndTheRoundStillEnds()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the held handle is Windows': a Unix process removes an empty folder others have open (the Unix twin is the scratch's own test, with a read-only parent)");
        var sink = new ListSink();
        var held = new System.Collections.Concurrent.ConcurrentBag<IDisposable>();
        var once = new Lock();
        var done = false;

        try
        {
            var round = await _h.RepairedCodeRoundAsync("none", log: LogInto(sink), beforeARepair: folder =>
            {
                lock (once)
                {
                    if (!done)
                    {
                        held.Add(FolderHolds.Undeletable(folder));
                        done = true;
                    }
                }
            });

            Verdict(round.Answer).Should().Be("proceed", "a removal that fails never fails the round");
            var folders = RoundFolders.Of(round);
            Directory.Exists(folders.Repair).Should().BeTrue("it could not be removed, so it is left to the six-hour sweep");
            WarningsAbout(sink, folders.Repair).Should().ContainSingle("one warning, naming the folder").Which.Should().Contain(SweepSentence);
            Directory.Exists(folders.Workspace).Should().BeFalse("the other folder was free, so it went");
        }
        finally
        {
            foreach (var hold in held)
            {
                hold.Dispose();
            }
        }
    }

    [Fact]
    public async Task ADescendantStillInTheEmptyFolder_DoesNotChangeTheRoundsOutcome_TheFolderIsGoneOnUnixAndLeftOnWindows()
    {
        var sink = new ListSink();
        var started = new List<FolderHolds.Descendant>();
        var once = new Lock();

        try
        {
            var round = await _h.RepairedCodeRoundAsync("none", log: LogInto(sink), beforeARepair: folder =>
            {
                lock (once)
                {
                    if (started.Count == 0)
                    {
                        started.Add(FolderHolds.StartDescendantIn(folder));
                    }
                }
            });

            var folders = RoundFolders.Of(round);
            started.Should().ContainSingle("a real child entered the repair folder, said ready and closed its pipes");
            Verdict(round.Answer).Should().Be("proceed", "a descendant that outlives its round changes nothing about the round");
            AssertTheFolderMetItsDescendant(sink, folders.Repair);
        }
        finally
        {
            started.ForEach(descendant => descendant.Dispose());
        }
    }

    /// <summary>Policy of D4: an EMPTY folder is removed after the round ended even with a descendant in it — a Unix removal succeeds, a Windows one is refused and left to the sweep.</summary>
    private static void AssertTheFolderMetItsDescendant(ListSink sink, string repair)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.Exists(repair).Should().BeTrue("a folder that is some process's working folder cannot be removed on Windows: left to the sweep");
            WarningsAbout(sink, repair).Should().ContainSingle().Which.Should().Contain(SweepSentence);

            return;
        }

        Directory.Exists(repair).Should().BeFalse("on Unix removing an empty folder a process is in succeeds: the round's end has the descendant's folder");
        WarningsAbout(sink, repair).Should().BeEmpty();
    }
}
