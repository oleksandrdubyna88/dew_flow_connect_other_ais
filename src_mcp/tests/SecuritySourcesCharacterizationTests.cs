using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Pins what <see cref="SecuritySources.ReadAsync(SecurityLaneSetting, IReadOnlyList{SecurityRun}, IReadOnlyList{FileDiff}, SourceResolver, CancellationToken)"/>
/// reads and records on every arm — nothing due, the per-file and per-hunk caps, duplicate windows, a read that
/// gives up on its own, the caller cancelling, and the collection deadline — so splitting it to complexity 4
/// cannot change what source a slice reviewer is given.
/// </summary>
/// <remarks>
/// <para>Written against the code BEFORE the split and observed green there
/// (todo/PLAN_security_lane_methods_within_complexity_4.md).</para>
/// <para>Real git, as <see cref="ASourceRequestIsServedOrRefusedTests"/> uses, so a served window is git's answer
/// and not a belief about it; a wrapping launcher only intervenes for the arms about giving up.</para>
/// <para>The deadline arm waits out the real 30-second collection budget: the budget is a constant of the lane,
/// and making it injectable to shorten this test would change the code under characterization.</para>
/// </remarks>
public sealed class SecuritySourcesCharacterizationTests : IAsyncLifetime
{
    private readonly ProcessLauncher _real = new();
    private TempGitRepo _git = null!;
    private string _head = string.Empty;
    private const string Deadline = "Source read deadline reached; remaining source omitted.";

    /// <summary>The real launcher, with a hook that may take over a request instead.</summary>
    private sealed class Gate(IProcessLauncher real) : IProcessLauncher
    {
        public Func<ProcessRequest, CancellationToken, Task<ProcessResult>?> Instead { get; set; } = (_, _) => null;
        public int Launched;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Launched);
            return Instead(request, ct) ?? real.RunAsync(request, ct);
        }
    }

    private static string Methods(string type) => $"class {type}\n{{\n" + string.Concat(Enumerable.Range(0, 6).Select(i =>
        $"    int M{i}(int value)\n    {{\n        var a = value;\n        var b = a + {i};\n        var c = b * 2;\n        var d = c - 1;\n        var e = d + a;\n        return e;\n    }}\n\n")) + "}\n";

    public async ValueTask InitializeAsync()
    {
        _git = await TempGitRepo.InitAsync(_real, "coai-security-sources-");
        Directory.CreateDirectory(Path.Combine(_git.Path, "src"));
        foreach (var name in new[] { "A", "B", "C" }) await _git.WriteAsync($"src/{name}.cs", Methods(name));
        await _git.CommitAsync("fixture");
        _head = await _git.HeadAsync();
    }

    public async ValueTask DisposeAsync() => await _git.DisposeAsync();

    private static SecurityLaneSetting Lane(string context) => SecurityLaneSetting.Parse(
        $$"""{"enabled":true,"runs":[{"vendor":"qwen","prompt":"redteam-sql","context":"{{context}}"}]}""",
        [new("qwen") { Runtime = "local" }]);

    /// <summary>A patch touching the given head lines, each its own hunk, with an SQL call so the preset triggers.</summary>
    private static string Patch(params int[] lines) =>
        string.Concat(lines.Select(line => $"@@ -{line},1 +{line},1 @@\n-old\n+database.Query(value);\n"));

    private Task<IReadOnlyDictionary<string, string>> Read(Gate gate, string context, FileDiff[] files, CancellationToken ct = default)
    {
        var lane = Lane(context);
        return SecuritySources.ReadAsync(lane, lane.Configured(Stage.CodeReview), files,
            new SourceResolver(new GitHistory(gate), new TreeSitterOutliner(), _git.Path, _head), ct);
    }

    /// <summary>Every entry in reading order, the fixture's commit id replaced: it changes with every run.</summary>
    internal static string Render(IReadOnlyDictionary<string, string> sources) => System.Text.RegularExpressions.Regex.Replace(
        string.Join("\n=====\n", sources.Select(pair => $"{pair.Key} =>\n{pair.Value}")), "[0-9a-f]{40}", "<sha>");

    [Fact]
    public async Task A_diff_pairing_reads_no_source()
    {
        var gate = new Gate(_real);
        (await Read(gate, "diff", [new("src/A.cs", Patch(4))])).Should().BeEmpty();
        gate.Launched.Should().Be(0);
    }

    [Fact]
    public async Task A_slice_pairing_whose_trigger_does_not_match_reads_no_source()
    {
        var gate = new Gate(_real);
        (await Read(gate, "slice", [new("src/A.cs", "@@ -4,1 +4,1 @@\n-old\n+return 1;\n")])).Should().BeEmpty();
        gate.Launched.Should().Be(0);
    }

    [Fact]
    public async Task At_most_four_hunks_are_read_and_a_repeated_window_is_kept_once()
    {
        // Hunks 1 and 2 share M0; hunk 5 (M3) and 6 (M4) lie beyond the four-hunk cap.
        var sources = await Read(new Gate(_real), "slice", [new("src/A.cs", Patch(5, 7, 16, 27, 38, 49))]);
        Render(sources).Should().Be(Expected["four hunks"]);
    }

    [Fact]
    public async Task Files_without_diff_text_are_skipped_and_sixteen_files_are_read()
    {
        FileDiff[] files = [new("config/server.pem", Patch(1)), .. Enumerable.Range(0, 18).Select(i => new FileDiff($"src/F{i:D2}.cs", Patch(5)))];
        var sources = await Read(new Gate(_real), "slice", files);
        sources.Keys.Should().Equal(Enumerable.Range(0, 16).Select(i => $"src/F{i:D2}.cs"));
    }

    [Fact]
    public async Task A_git_read_that_times_out_is_the_resolvers_refusal_and_the_next_file_is_still_read()
    {
        var gate = new Gate(_real)
        {
            Instead = (request, _) => request.Arguments.Any(a => a.Contains("src/A.cs", StringComparison.Ordinal))
                ? Task.FromException<ProcessResult>(new OperationCanceledException("fixture: the read gave up"))
                : null,
        };
        var sources = await Read(gate, "slice", [new("src/A.cs", Patch(5, 16)), new("src/B.cs", Patch(5))]);
        Render(sources).Should().Be(Expected["gave up"]);
    }

    [Fact]
    public async Task The_caller_cancelling_mid_read_is_a_cancellation_not_an_omission()
    {
        using var caller = new CancellationTokenSource();
        var gate = new Gate(_real)
        {
            Instead = (_, ct) =>
            {
                caller.Cancel();
                return Task.FromException<ProcessResult>(new OperationCanceledException(ct));
            },
        };
        var read = () => Read(gate, "slice", [new("src/A.cs", Patch(5)), new("src/B.cs", Patch(5))], caller.Token);
        await read.Should().ThrowAsync<OperationCanceledException>();
        gate.Launched.Should().Be(1);
    }

    [Fact]
    public async Task The_collection_deadline_ends_the_file_it_interrupts_and_every_later_one()
    {
        var gate = new Gate(_real)
        {
            Instead = (request, ct) => request.Arguments.Any(a => a.Contains("src/B.cs", StringComparison.Ordinal))
                ? Task.Delay(Timeout.Infinite, ct).ContinueWith<ProcessResult>(_ => throw new OperationCanceledException(ct), TaskScheduler.Default)
                : null,
        };
        var sources = await Read(gate, "slice", [new("src/A.cs", Patch(5)), new("src/B.cs", Patch(5, 16)), new("src/C.cs", Patch(5))]);
        Render(sources).Should().Be(Expected["deadline"]);
    }

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["deadline"] = "src/A.cs =>\n### src/A.cs lines 3-11 of 63 @ <sha> (why: changed code; partial source context)\n```csharp\n    int M0(int value)\n    {\n        var a = value;\n        var b = a + 0;\n        var c = b * 2;\n        var d = c - 1;\n        var e = d + a;\n        return e;\n    }\n```\n\n\n=====\nsrc/B.cs =>\nSource read deadline reached; remaining source omitted.",
        ["four hunks"] = "src/A.cs =>\n### src/A.cs lines 3-11 of 63 @ <sha> (why: changed code; partial source context)\n```csharp\n    int M0(int value)\n    {\n        var a = value;\n        var b = a + 0;\n        var c = b * 2;\n        var d = c - 1;\n        var e = d + a;\n        return e;\n    }\n```\n\n\n### src/A.cs lines 13-21 of 63 @ <sha> (why: changed code; partial source context)\n```csharp\n    int M1(int value)\n    {\n        var a = value;\n        var b = a + 1;\n        var c = b * 2;\n        var d = c - 1;\n        var e = d + a;\n        return e;\n    }\n```\n\n\n### src/A.cs lines 23-31 of 63 @ <sha> (why: changed code; partial source context)\n```csharp\n    int M2(int value)\n    {\n        var a = value;\n        var b = a + 2;\n        var c = b * 2;\n        var d = c - 1;\n        var e = d + a;\n        return e;\n    }\n```\n\n",
        ["gave up"] = "src/A.cs =>\nnot served: src/A.cs — timed out: git did not answer within 30 s; ask again next turn\n\n=====\nsrc/B.cs =>\n### src/B.cs lines 3-11 of 63 @ <sha> (why: changed code; partial source context)\n```csharp\n    int M0(int value)\n    {\n        var a = value;\n        var b = a + 0;\n        var c = b * 2;\n        var d = c - 1;\n        var e = d + a;\n        return e;\n    }\n```\n\n",
    };
}
