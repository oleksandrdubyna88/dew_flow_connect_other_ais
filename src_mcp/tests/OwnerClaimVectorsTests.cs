using System.Text.Json;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The C# writer of the owner claim agrees with the Node writer and the future reader, because both read ONE golden file,
/// <c>shared/owner-claim-vectors.json</c> (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, E2 code gate G0/G1 and judge J1/J2).
/// </summary>
/// <remarks>
/// <para>Two writers of one format — <see cref="OwnerClaim"/> here, <c>claimText</c> in <c>src_vs_code/scripts/run-tests.mjs</c> — and one reader to come
/// (E5's Node sweeper) can drift apart without any test noticing: each side's own tests pass against its own idea of the format. The vectors are the
/// shared idea. <c>runTemp.test.mjs</c> reads the same file; change one side's format and only that side goes red. <b>E5's reader must pass the same vectors.</b></para>
/// <para><b>The host spelling is the vector that would have defeated the claim</b> (judge J1): the C# writer used <c>Environment.MachineName</c> (NetBIOS: upper case, cut
/// at 15 characters, no domain) and Node's <c>os.hostname()</c> is the OS host name, so the same machine was <c>BUILD01</c> to one and <c>build01</c> to the other and a reader
/// would have judged every C# claim "another host" and kept it for ever. The live test below asks Node for its own spelling.</para>
/// </remarks>
public sealed class OwnerClaimVectorsTests
{
    private static JsonElement Vectors { get; } = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "owner-claim-vectors.json"))).RootElement;

    public static TheoryData<string, string, int, long?, string> Formats()
    {
        var rows = new TheoryData<string, string, int, long?, string>();
        foreach (var row in Vectors.GetProperty("format").EnumerateArray())
        {
            rows.Add(
                row.GetProperty("host").GetString()!, row.GetProperty("os").GetString()!, row.GetProperty("pid").GetInt32(),
                row.TryGetProperty("start", out var start) ? start.GetInt64() : null, row.GetProperty("text").GetString()!);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void TheWriter_ProducesExactlyTheVectorsText(string host, string os, int pid, long? start, string expected) =>
        OwnerClaim.Format(host, os, pid, start).Should().Be(expected);

    public static TheoryData<string> Valid() => [.. Vectors.GetProperty("valid").EnumerateArray().Select(e => e.GetString()!)];

    public static TheoryData<string> Invalid() => [.. Vectors.GetProperty("invalid").EnumerateArray().Select(e => e.GetString()!)];

    [Theory]
    [MemberData(nameof(Valid))]
    public void AStrictReader_AcceptsEveryValidVector(string text) => OwnerClaim.IsWellFormed(text).Should().BeTrue();

    [Theory]
    [MemberData(nameof(Invalid))]
    public void AStrictReader_RefusesEveryInvalidVector(string text) => OwnerClaim.IsWellFormed(text).Should().BeFalse();

    public static TheoryData<string, string, long?> LinuxStarts()
    {
        var rows = new TheoryData<string, string, long?>();
        foreach (var row in Vectors.GetProperty("linuxStart").EnumerateArray())
        {
            var seconds = row.GetProperty("startSeconds");
            rows.Add(row.GetProperty("stat").GetString()!, row.GetProperty("procStat").GetString()!, seconds.ValueKind == JsonValueKind.Null ? null : seconds.GetInt64());
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(LinuxStarts))]
    public void TheLinuxStart_IsComputedByTheFormulaBothWritersShare(string stat, string procStat, long? expected) =>
        OwnerClaim.LinuxStartSeconds(stat, procStat).Should().Be(expected);

    [Fact]
    public async Task ThisMachinesHost_IsSpelledAsNodeSpellsIt()
    {
        var node = await new ProcessLauncher().RunAsync(
            new ProcessRequest("node", ["-p", "require('os').hostname()"], AppContext.BaseDirectory) { Timeout = TimeSpan.FromSeconds(30) },
            TestContext.Current.CancellationToken);
        Assert.SkipWhen(node.ExitCode != 0 || node.StdOut.Trim().Length == 0, "node is not on PATH here, so its host spelling cannot be asked: " + node.StdErr);

        var claimHost = OwnerClaim.ForThisProcess().Split('@')[0];

        claimHost.Should().Be(OwnerClaim.Format(node.StdOut.Trim(), "linux", 1, null).Split('@')[0],
            "the Node sweeper compares this host with its own os.hostname(); a different spelling makes every claim 'another host'");
    }
}
