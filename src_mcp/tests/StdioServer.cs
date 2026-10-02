using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A real <c>coai-mcp</c> process over real stdio, for the suites that prove the WIRE.
/// </summary>
/// <remarks>
/// Extracted from <see cref="McpContractTests"/> the day a second suite needed it (<c>AskHumanScenarioTests</c>,
/// S3 of the question consultant) rather than copied there — the contract tests keep their own one-line
/// <c>Start</c> over this, so nothing they assert moved.
/// </remarks>
internal static class StdioServer
{
    /// <summary>Starts the server this build produced, on a data directory of the caller's, with the environment a test needs.</summary>
    internal static Process Start(
        string dataDir,
        string logLevel = "debug",
        int escalationSeconds = 1800,
        params (string Name, string Value)[] alsoInTheEnvironment)
    {
        var info = new ProcessStartInfo(ServerBinary.Path)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.Environment["COAI_DATA_DIR"] = dataDir;
        foreach (var (name, value) in alsoInTheEnvironment)
        {
            info.Environment[name] = value;
        }

        info.Environment["COAI_LOG_LEVEL"] = logLevel; // chatty on purpose: purity is the claim
        info.Environment["COAI_ESCALATION_SECONDS"] = escalationSeconds.ToString();
        // These test the WIRE. Translation is a vendor call with its own tests; leaving it on
        // would make every escalation here wait on a real model.
        info.Environment["COAI_TRANSLATOR_PROVIDER"] = "none";

        return Process.Start(info)!;
    }

    /// <summary>One request, one answer line — the server must answer before the client gives up.</summary>
    internal static async Task<JsonDocument> RoundTrip(Process server, string request, int timeoutSeconds = 30)
    {
        await server.StandardInput.WriteLineAsync(request);
        await server.StandardInput.FlushAsync();
        var line = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
        line.Should().NotBeNull("the server must answer before the client gives up");

        return JsonDocument.Parse(line!);
    }

    /// <summary>The handshake every tool call needs first: initialize, then the initialized notification.</summary>
    internal static async Task InitializeAsync(Process server, string client = "contract-test")
    {
        await RoundTrip(server,
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"" + client + "\",\"version\":\"0\"}}}");
        await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        await server.StandardInput.FlushAsync();
    }

    /// <summary>A tool call's own answer — the JSON the tool wrote, not the SDK's envelope.</summary>
    internal static async Task<JsonElement> Call(Process server, int id, string tool, string argumentsJson, int timeoutSeconds = 60)
    {
        var answer = await RoundTrip(server,
            "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"" + tool + "\",\"arguments\":" + argumentsJson + "}}",
            timeoutSeconds);
        answer.RootElement.TryGetProperty("result", out var result)
            .Should().BeTrue($"the call must be answered by the tool, not by the SDK: {answer.RootElement}");

        return JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
    }

    /// <summary>Ends the server the way every stdio test does: kill the tree, wait for the exit.</summary>
    internal static async Task StopAsync(Process server)
    {
        server.Kill(entireProcessTree: true);
        await server.WaitForExitAsync();
    }
}
