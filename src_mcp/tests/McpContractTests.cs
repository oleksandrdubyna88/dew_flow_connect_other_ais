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

    private Process Start(string logLevel = "debug", int escalationSeconds = 1800)
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
        info.Environment["COAI_ESCALATION_SECONDS"] = escalationSeconds.ToString();
        // These test the WIRE. Translation is a vendor call with its own tests; leaving it on
        // would make every escalation here wait on a real model.
        info.Environment["COAI_TRANSLATOR_PROVIDER"] = "none";
        var process = Process.Start(info)!;
        return process;
    }

    private static async Task<JsonDocument> RoundTrip(Process server, string request, int timeoutSeconds = 30)
    {
        await server.StandardInput.WriteLineAsync(request);
        await server.StandardInput.FlushAsync();
        var line = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
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

    /// <summary>
    /// With nobody at the keyboard, the escalation waits its budget and then tells the model to
    /// ask in the chat — the family's `remote-ask` fallback, observed over the real wire.
    /// </summary>
    [Fact]
    public async Task AskHuman_WithNobodyListening_WaitsThenSaysToAskInTheChat()
    {
        using var server = Start(escalationSeconds: 3);
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            // The budget is short here; the read must still outlast it.
            var answer = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ask_human","arguments":{"repoPath":"D:/nowhere","branch":"main","question":"ship it?"}}}
                """, timeoutSeconds: 60);
            var text = answer.RootElement.GetProperty("result").GetProperty("content")[0]
                .GetProperty("text").GetString();

            text.Should().Contain("no_answer_yet").And.Contain("ask the person directly");
            Directory.GetFiles(Path.Combine(_data, "escalations"), "*.json")
                .Should().ContainSingle("the question stays open — a person may still answer it");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task AskHuman_AnsweredInVsCode_ReturnsThePersonsWords()
    {
        using var server = Start(escalationSeconds: 60);
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            await server.StandardInput.WriteLineAsync("""
                {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"ask_human","arguments":{"repoPath":"D:/nowhere","branch":"main","question":"ship it?"}}}
                """);
            await server.StandardInput.FlushAsync();

            // Stand in for the extension: wait for the question file, then write the answer.
            var dir = Path.Combine(_data, "escalations");
            string? question = null;
            for (var i = 0; i < 200 && question is null; i++)
            {
                question = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*.json").FirstOrDefault(f => !f.EndsWith(".answer.json"))
                    : null;
                if (question is null)
                {
                    await Task.Delay(50);
                }
            }

            question.Should().NotBeNull("the question must appear immediately, not when the wait ends");
            var id = Path.GetFileNameWithoutExtension(question!);
            await File.WriteAllTextAsync(
                Path.Combine(dir, $"{id}.answer.json"),
                $$"""{"id":"{{id}}","answer":"no, fix it first","answeredUtc":"{{DateTime.UtcNow:O}}"}""");

            var line = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
            var text = JsonDocument.Parse(line!).RootElement
                .GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();

            text.Should().Contain("answered").And.Contain("no, fix it first");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task Resolve_WithoutTheHumanOverride_IsAnAnswer_NotAnSdkError()
    {
        // The ordinary path of EVERY round: record decisions, no override. Found live in WSL —
        // `humanDecision` had no default, so the SDK made it REQUIRED and a normal resolve came
        // back as "An error occurred invoking 'resolve'". The Windows live run had missed it by
        // always passing the override, which is the one call that does not need to work.
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"contract-test","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var answer = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"resolve","arguments":{"repoPath":"D:/nowhere","branch":"main","decisions":"[]"}}}
                """);

            var text = answer.RootElement.GetProperty("result").GetProperty("content")[0]
                .GetProperty("text").GetString();
            text.Should().NotBeNull();
            // No session for that repo, so the honest answer is our own sentence — the point is
            // that it IS our sentence, in JSON, and not an invocation failure.
            var parsed = JsonDocument.Parse(text!);
            parsed.RootElement.TryGetProperty("error", out var sentence).Should().BeTrue();
            sentence.GetString().Should().Contain("call open first");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }
}
