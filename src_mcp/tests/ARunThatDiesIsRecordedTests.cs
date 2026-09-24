using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The death, end to end: a real server killed with no chance to say anything, and the next real
/// server writing it down — once.
/// </summary>
/// <remarks>
/// <para><see cref="TheRunsAreAccountedForTests"/> holds the planner and the order of the disk
/// operations. What only a real process can show is that the HOST does it: that <c>ServeAsync</c>
/// writes a marker at all, sweeps on start, and clears its own on a clean exit. Every piece being
/// right and the host never calling them is the defect a unit test cannot see. (Story 3.1.)</para>
/// <para>The heartbeat is backdated by hand rather than waited for. Thirty minutes of silence is the
/// rule, and the rule is the planner's, tested there; this asks only whether the host applies it.</para>
/// </remarks>
[Collection("fakecli-env")] // the server child inherits our env; keep FAKECLI_* quiet around it
public sealed class ARunThatDiesIsRecordedTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _data = Directory.CreateTempSubdirectory("coai-deaths-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory that outlives one run is litter, not a failed test.
        }
    }

    private string Runs => Path.Combine(_data, RunMarkers.Folder);

    private string NoticesFile => Path.Combine(_data, ServerNotices.Name);

    private string[] Markers() =>
        Directory.Exists(Runs) ? Directory.GetFiles(Runs, "*.json") : [];

    private string[] UncleanExits() => Notices(ServerNoticeCodes.UncleanExit);

    private Process Start()
    {
        var info = new ProcessStartInfo(ServerBinary.Path)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.Environment["COAI_DATA_DIR"] = _data;
        info.Environment.Remove("COAI_DATA_SIDE"); // the data dir is exactly this folder
        info.Environment["COAI_TRANSLATOR_PROVIDER"] = "none";

        var process = Process.Start(info)!;
        // Drained, so a chatty stderr can never fill its pipe and stall the child.
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static Task<bool> Eventually(Func<bool> condition) => Polls.Until(condition, Patience);

    /// <summary>The marker of the one run that started since <paramref name="before"/>.</summary>
    private async Task<string> MarkerOf(Process server, string[] before)
    {
        (await Eventually(() => Markers().Except(before).Any()))
            .Should().BeTrue($"a serving run writes runs/<run>.json at start (pid {server.Id})");

        return Markers().Except(before).Single();
    }

    /// <summary>
    /// A real refusal over stdio — <c>open</c> on a folder that is no repository, as the seam's own leg
    /// provokes one — so the run leaves a notice of its OWN behind before it dies.
    /// </summary>
    private async Task<JsonElement> ARefusalWrittenBy(Process server)
    {
        await Said(server, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"deaths-test","version":"0"}}}""");
        await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        var nowhere = JsonSerializer.Serialize(Path.Combine(_data, "not-a-repository"));
        await Said(server, """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"open","arguments":{"repoPath":"""
            + nowhere + ""","branch":"main"}}}""");

        (await Eventually(() => Notices(ServerNoticeCodes.Refused).Length > 0))
            .Should().BeTrue("open on a folder that is no repository is refused, and a refusal is written down");

        return JsonDocument.Parse(Notices(ServerNoticeCodes.Refused).Single()).RootElement;
    }

    private string[] Notices(string code) => NoticeLines.Of(NoticesFile, code);

    private static async Task Said(Process server, string request)
    {
        await server.StandardInput.WriteLineAsync(request);
        await server.StandardInput.FlushAsync();
    }

    private static async Task<int> EndedCleanly(Process server)
    {
        server.StandardInput.Close(); // the client hangs up — the ordinary end of an MCP server
        using var budget = new CancellationTokenSource(Patience);
        await server.WaitForExitAsync(budget.Token);

        return server.ExitCode;
    }

    private static void Backdated(string marker, TimeSpan by)
    {
        var json = JsonNode.Parse(File.ReadAllText(marker))!.AsObject();
        json["heartbeatUtc"] = DateTime.UtcNow.Subtract(by).ToString("O");
        File.WriteAllText(marker, json.ToJsonString());
    }

    [Fact]
    public async Task AKilledServer_IsRecordedOnce_ByTheNextStart_AndACleanExitLeavesNothingBehind()
    {
        // A: started, then killed — no catch, no finally, nothing written on the way out.
        var killed = Start();
        string dead;
        string? ownRun;
        try
        {
            dead = await MarkerOf(killed, []);
            ownRun = (await ARefusalWrittenBy(killed)).GetProperty("run").GetString();
        }
        finally
        {
            killed.Kill(entireProcessTree: true);
            await killed.WaitForExitAsync();
        }

        var deadRun = Path.GetFileNameWithoutExtension(dead);
        Backdated(dead, TimeSpan.FromHours(1));

        // B: finds the death, records it, and on a clean exit clears its OWN marker.
        var finder = Start();
        await MarkerOf(finder, [dead]);
        (await Eventually(() => UncleanExits().Length > 0))
            .Should().BeTrue("the next start records a run that went silent without ending");
        (await EndedCleanly(finder)).Should().Be(0);

        var recorded = JsonDocument.Parse(UncleanExits().Single()).RootElement;
        recorded.GetProperty("subject").GetString().Should().Be(deadRun, "the death is named by the DEAD run's id");
        recorded.GetProperty("run").GetString().Should().Be(deadRun, "and joins to it, not to the run that found it");
        ownRun.Should().Be(deadRun,
            "the marker is named for the id the run stamped on its own records, or the death joins nothing");
        recorded.GetProperty("pid").GetInt32().Should().Be(killed.Id);
        Markers().Should().BeEmpty("the dead run's marker is retired and the finder's is cleared on its clean exit");

        // C: a third start has nothing to record — the death was written down once, not once per start.
        var third = Start();
        await MarkerOf(third, []);
        (await EndedCleanly(third)).Should().Be(0);

        UncleanExits().Should().ContainSingle("one death, one record, however many starts come after it");
        Markers().Should().BeEmpty();
    }
}
