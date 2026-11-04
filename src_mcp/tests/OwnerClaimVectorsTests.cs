using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The C# reader and the Node reader of the owner record agree on every row of <c>shared/owner-record-cross-vectors.json</c>, and on the live claim of
/// this very run (E5 S3, plan row 15). Both read the contract's grammar <c>&lt;owner&gt;:&lt;host&gt;@&lt;os&gt;:&lt;pid&gt;[:&lt;start&gt;[:&lt;ns&gt;]]</c>; the old
/// <c>&lt;host&gt;@win32|darwin|linux</c> spellings are unreadable to both.
/// </summary>
/// <remarks>
/// <c>shared/owner-claim-vectors.json</c> is deleted (E5 S4): its one reader, the Node <c>runTemp.test.mjs</c>, went with the legacy writer. This file stopped reading it in S3.
/// The Node reader is asked ONCE, by one <c>node --input-type=module -e</c> that imports the conventions' own <c>owned.mjs</c>.
/// </remarks>
public sealed class OwnerClaimVectorsTests
{
    private const string AskNode = """
        import { parseOwner, parseLegacyOwner, ownerState } from process.argv[1];
        const rows = JSON.parse(process.argv[2]);
        const verdict = (read, text) => (read(text).unreadable ? "unreadable" : "owner");
        process.stdout.write(JSON.stringify(rows.map((text) => ({
          text,
          node: verdict(parseOwner, text),
          nodeLegacy: parseLegacyOwner(text).unreadable ? "unreadable" : "legacy",
          state: ownerState(parseOwner(text)),
        }))));
        """;

    private sealed record Row(string Text, string Csharp, string Node, string NodeLegacy);

    private static List<Row> Rows()
    {
        var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "owner-record-cross-vectors.json"))).RootElement;

        return
        [
            .. vectors.GetProperty("rows").EnumerateArray().Select(row => new Row(
                row.GetProperty("text").GetString()!, row.GetProperty("csharp").GetString()!, row.GetProperty("node").GetString()!, row.GetProperty("nodeLegacy").GetString()!)),
        ];
    }

    private static string CSharpVerdict(string text) => Owner.Parse(text) is null ? "unreadable" : "owner";

    private static async Task<Dictionary<string, (string Node, string Legacy, string State)>> NodeVerdictsAsync(IEnumerable<string> texts)
    {
        var owned = new Uri(Path.Combine(RepoPaths.Root(), ".agents", "conventions", "tools", "lib", "owned.mjs")).AbsoluteUri;
        var script = AskNode.Replace("process.argv[1]", $"\"{owned}\"", StringComparison.Ordinal).Replace("process.argv[2]", "process.argv[1]", StringComparison.Ordinal);
        var node = await new ProcessLauncher().RunAsync(
            new ProcessRequest("node", ["--input-type=module", "-e", script, "--", JsonSerializer.Serialize(texts)], AppContext.BaseDirectory) { Timeout = TimeSpan.FromSeconds(60) },
            TestContext.Current.CancellationToken);
        Assert.SkipWhen(node.ExitCode != 0 && node.StdErr.Contains("not found", StringComparison.OrdinalIgnoreCase), "node is not on PATH here, so the Node reader cannot be asked: " + node.StdErr);
        node.ExitCode.Should().Be(0, node.StdErr);

        return JsonDocument.Parse(node.StdOut).RootElement.EnumerateArray().ToDictionary(
            e => e.GetProperty("text").GetString()!, e => (e.GetProperty("node").GetString()!, e.GetProperty("nodeLegacy").GetString()!, e.GetProperty("state").GetString()!));
    }

    [Fact]
    public async Task OldAndNewClaims_AreReadByBothReaders()
    {
        var rows = Rows();
        var live = File.ReadAllText(RunTemp.Folder + ".owner");
        var asked = await NodeVerdictsAsync([.. rows.Select(r => r.Text), live]);

        foreach (var row in rows)
        {
            CSharpVerdict(row.Text).Should().Be(row.Csharp, $"C# on '{row.Text}'");
            asked[row.Text].Node.Should().Be(row.Node, $"Node's parseOwner on '{row.Text}'");
            asked[row.Text].Legacy.Should().Be(row.NodeLegacy, $"Node's parseLegacyOwner on '{row.Text}'");
        }

        CSharpVerdict(live).Should().Be("owner", $"the live claim '{live}' is in the contract's grammar");
        asked[live].Node.Should().Be("owner", "and Node reads it too");
        // Reading it is not judging it: Node's liveness (the conventions' ownerState: same owner, same host spelling, same pid namespace, this pid alive, the
        // START within two seconds of the process's own) must call this run's claim LIVE. A start field computed by another formula than the one both writers share
        // would read "unknown" here — the guarantee the removed TheLinuxStart_IsComputedByTheFormulaBothWritersShare gave.
        asked[live].State.Should().Be("live", "the claim of a running process, in the C# writer's own spelling, is live to the Node judge");
    }

    [Fact]
    public async Task ThisMachinesHost_IsSpelledAsNodeSpellsIt()
    {
        var owned = new Uri(Path.Combine(RepoPaths.Root(), ".agents", "conventions", "tools", "lib", "owned.mjs")).AbsoluteUri;
        var node = await new ProcessLauncher().RunAsync(
            new ProcessRequest("node", ["--input-type=module", "-e", $"import {{ thisOwner }} from \"{owned}\"; process.stdout.write(thisOwner().host);"], AppContext.BaseDirectory)
            {
                Timeout = TimeSpan.FromSeconds(30),
            },
            TestContext.Current.CancellationToken);
        Assert.SkipWhen(node.ExitCode != 0 || node.StdOut.Trim().Length == 0, "node is not on PATH here, so its host spelling cannot be asked: " + node.StdErr);

        Owner.This.Host.Should().Be(node.StdOut.Trim(),
            "the Node sweeper compares this host with its own thisOwner().host; a different spelling makes every claim 'another host'");
    }
}
