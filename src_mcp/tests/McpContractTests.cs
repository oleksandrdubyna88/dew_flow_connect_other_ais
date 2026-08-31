using System.Diagnostics;
using System.Text.Json;
using Xunit;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The wire itself: a real <c>coai-mcp</c> process, real stdio, real JSON-RPC. What these prove
/// no in-process test can — the handshake works, the seven tools are advertised, and stdout
/// carries nothing but protocol.
/// </summary>
[Collection("fakecli-env")] // the server child inherits our env; keep FAKECLI_* quiet around it
public sealed class McpContractTests : IDisposable
{
    private static string ServerExe
    {
        get
        {
            // COAI_CONTRACT_EXE points these tests at a PUBLISHED binary — the release smoke.
            if (Environment.GetEnvironmentVariable("COAI_CONTRACT_EXE") is { Length: > 0 } published)
            {
                return published;
            }

            // tests/bin/<cfg>/net10.0 → src/bin/<cfg>/net10.0/coai-mcp
            var configuration = AppContext.BaseDirectory.Contains("Release") ? "Release" : "Debug";
            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "src", "bin", configuration, "net10.0",
                OperatingSystem.IsWindows() ? "coai-mcp.exe" : "coai-mcp"));
        }
    }

    private readonly string _data = Directory.CreateTempSubdirectory("coai-contract-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private Process Start(string logLevel = "debug")
    {
        var info = new ProcessStartInfo(ServerExe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.Environment["COAI_DATA_DIR"] = _data;
        info.Environment["COAI_LOG_LEVEL"] = logLevel; // chatty on purpose: purity is the claim
        var process = Process.Start(info)!;
        return process;
    }

    private static async Task<JsonDocument> RoundTrip(Process server, string request)
    {
        await server.StandardInput.WriteLineAsync(request);
        await server.StandardInput.FlushAsync();
        var line = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
        line.Should().NotBeNull("the server must answer before the client gives up");
        return JsonDocument.Parse(line!);
    }

    [Fact]
    public async Task Initialize_ThenToolsList_NamesTheSevenTools_AndStdoutStaysPure()
    {
        using var server = Start();
        try
        {
            var init = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"contract-test","version":"0"}}}
                """);
            init.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name")
                .GetString().Should().Be("connect-other-ais");

            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var tools = await RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var names = tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(t => t.GetProperty("name").GetString())
                .ToList();

            names.Should().BeEquivalentTo(
                ["providers", "open", "review_plan", "review_code", "resolve", "status", "ask_human"],
                "the seven tools, unprefixed — the client's `coai` namespace is the only one");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task EveryStdoutLine_ParsesAsJson_EvenWithVerboseLogging()
    {
        using var server = Start(logLevel: "verbose");
        try
        {
            await server.StandardInput.WriteLineAsync("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            await server.StandardInput.FlushAsync();

            var lines = new List<string>();
            for (var i = 0; i < 2; i++)
            {
                var line = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
                if (line is { Length: > 0 })
                {
                    lines.Add(line);
                }
            }

            lines.Should().NotBeEmpty();
            foreach (var line in lines)
            {
                var parse = () => JsonDocument.Parse(line);
                parse.Should().NotThrow($"stdout carries the protocol and nothing else, got: {line[..Math.Min(80, line.Length)]}");
            }
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task AskHuman_ReturnsTheRefusal_TheMainAiMustSurface()
    {
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var answer = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ask_human","arguments":{"question":"ship it?"}}}
                """);
            var text = answer.RootElement.GetProperty("result").GetProperty("content")[0]
                .GetProperty("text").GetString();

            text.Should().Contain("SURFACE THIS QUESTION").And.Contain("ship it?");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }
}
