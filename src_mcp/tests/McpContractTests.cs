using System.Diagnostics;
using System.Text.Json;
using Xunit;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The wire itself: a real <c>coai-mcp</c> process, real stdio, real JSON-RPC. What these prove
/// no in-process test can — the handshake works, the eleven tools are advertised, and stdout
/// carries nothing but protocol.
/// </summary>
[Collection("fakecli-env")] // the server child inherits our env; keep FAKECLI_* quiet around it
public sealed class McpContractTests : IDisposable
{
    private static string ServerExe => ServerBinary.Path;

    private readonly string _data = Directory.CreateTempSubdirectory("coai-contract-").FullName;

    public void Dispose()
    {
        foreach (var directory in (string[])[_data, .. _repos])
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A temp directory that outlives one run is litter, not a failed test.
            }
        }
    }

    private Process Start(
        string logLevel = "debug",
        int escalationSeconds = 1800,
        params (string Name, string Value)[] alsoInTheEnvironment)
    {
        var info = new ProcessStartInfo(ServerExe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.Environment["COAI_DATA_DIR"] = _data;
        foreach (var (name, value) in alsoInTheEnvironment)
        {
            info.Environment[name] = value;
        }

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
    public async Task Initialize_ThenToolsList_NamesTheElevenTools_AndStdoutStaysPure()
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
                ["providers", "open", "review_plan", "review_code", "review_document", "review_feature", "resolve",
                 "status", "ask_human", "consult", "close_consult"],
                "the eleven tools, unprefixed — the client's `coai` namespace is the only one");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    /// <summary>
    /// The prompt is advertised, and asking for it yields a message addressed to the assistant.
    /// </summary>
    /// <remarks>
    /// <para>Over the WIRE, because that is the only place this can be wrong: the collection is
    /// registered in `ServeAsync` and nothing in-process would notice it being registered on an
    /// options object the transport never sees. A client lists this as `/mcp__coai__consult`.</para>
    /// <para>The argument is asked for WITHOUT a value as well, because that is the ordinary use —
    /// a person who types the command alone is saying "you know what we are stuck on".</para>
    /// </remarks>
    [Fact]
    public async Task PromptsList_NamesTheConsultPrompt_AndGettingItYieldsTheInstruction()
    {
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"contract-test","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var listed = await RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"prompts/list"}""");
            var prompts = listed.RootElement.GetProperty("result").GetProperty("prompts").EnumerateArray()
                .Select(p => p.GetProperty("name").GetString())
                .ToList();

            prompts.Should().BeEquivalentTo(["consult"], "one prompt, unprefixed, like the tools beside it");

            var got = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":3,"method":"prompts/get","params":{"name":"consult","arguments":{"problem":"The parser returns 3 where 4 is expected."}}}
                """);
            var message = got.RootElement.GetProperty("result").GetProperty("messages")[0];

            message.GetProperty("role").GetString().Should().Be("user");
            var text = message.GetProperty("content").GetProperty("text").GetString()!;
            text.Should().Contain("The parser returns 3 where 4 is expected.", "the person's own words travel unrewritten")
                .And.Contain("`consult` tool")
                .And.Contain("rev-parse --show-toplevel")
                .And.Contain("MATERIAL")
                .And.Contain("Report the verification back");

            // And with no words at all, which is the ordinary use. BOTH shapes of it: an empty string,
            // and the argument OMITTED — a client sends the second when somebody types the command
            // alone, and a registration that made the parameter required would fail exactly there
            // while every other test stayed green. (codex, story 5's plan round.)
            foreach (var arguments in (string[])["{\"problem\":\"\"}", "{}"])
            {
                var bare = (await RoundTrip(server,
                    $$$"""{"jsonrpc":"2.0","id":4,"method":"prompts/get","params":{"name":"consult","arguments":{{{arguments}}}}}""")).RootElement;

                bare.TryGetProperty("error", out _).Should().BeFalse($"arguments {arguments} must be accepted: {bare}");
                bare.GetProperty("result").GetProperty("messages")[0]
                    .GetProperty("content").GetProperty("text").GetString()
                    .Should().Contain("expected to know it");
            }

            // A problem carrying punctuation and a code span travels unrewritten, which is the claim
            // the message makes about the person's words.
            var awkward = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":5,"method":"prompts/get","params":{"name":"consult","arguments":{"problem":"`Count()` returns 3 after two fixes; expected 4."}}}
                """);
            awkward.RootElement.GetProperty("result").GetProperty("messages")[0]
                .GetProperty("content").GetProperty("text").GetString()
                .Should().Contain("`Count()` returns 3 after two fixes; expected 4.");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    /// <summary>
    /// A client that never asks for prompts still learns the consultant exists.
    /// </summary>
    /// <remarks>
    /// The instructions are the fallback, and nothing asserted they travel: `ServerInstructions` is
    /// set on an options object, and an edit that stopped setting it would leave both prompt tests
    /// green with the fallback silently gone. This reads the INITIALIZE response, which is the only
    /// place a client sees them. (codex, story 5's plan round.)
    /// </remarks>
    [Fact]
    public async Task Initialize_CarriesTheInstructions_IncludingTheConsultPrompt()
    {
        using var server = Start();
        try
        {
            var init = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"contract-test","version":"0"}}}
                """);

            var instructions = init.RootElement.GetProperty("result").GetProperty("instructions").GetString()!;

            instructions.Should()
                .Contain("`consult` is for when YOU are stuck", "the tool has to be named for a client that lists no prompts")
                .And.Contain("PROMPT", "and so does the other way in")
                .And.Contain("advice, not orders");
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

    /// <summary>
    /// The handshake's client and version, and the model the caller declared, reach the session.
    /// </summary>
    /// <remarks>
    /// <para>Asserted over the REAL wire because that is the only place the claim can be checked:
    /// <c>clientInfo</c> is read from the request-scoped <c>McpServer</c> the SDK injects into the
    /// tool lambda, and no in-process test exercises the injection. The plan round asked for exactly
    /// this (gemini, Major — "verify ClientInfo is accessible within the tool delegate scope").</para>
    /// <para>The version is DISTINCTIVE on purpose: with a name-only assertion, an implementation
    /// that recorded the client and dropped the version would pass. (codex, Major.)</para>
    /// <para>And the environment is set to a DIFFERENT vendor than the handshake, which is the
    /// precedence the plan round settled: the variables are the process's, inherited from whatever
    /// launched this server; the handshake belongs to the connection that is calling.</para>
    /// </remarks>
    [Fact]
    public async Task TheHandshakeAndTheDeclaredModel_ReachTheSessionFile()
    {
        var repo = await ARepository();
        using var server = Start(alsoInTheEnvironment: ("CLAUDE_CODE_SESSION_ID", "from-the-launcher"));
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"codex","version":"7.3.1"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var opened = await RoundTrip(server, OpenCall(repo, "codex-astra"), timeoutSeconds: 60);
            opened.RootElement.TryGetProperty("result", out var result)
                .Should().BeTrue($"the call must be answered, not refused: {opened.RootElement}");
            result.GetProperty("content")[0].GetProperty("text")
                .GetString().Should().NotContain("\"error\"", "the session must actually open");

            var caller = OnlySessionFile().GetProperty("caller");
            caller.GetProperty("client").GetString().Should().Be("codex");
            caller.GetProperty("clientVersion").GetString().Should().Be("7.3.1");
            caller.GetProperty("model").GetString().Should().Be("codex-astra");
            caller.GetProperty("vendor").GetString().Should().Be(
                "codex", "the handshake belongs to this connection; the variable belongs to whatever launched us");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    /// <summary>
    /// A client that sends no model is recorded as having sent none — never as a default.
    /// </summary>
    /// <remarks>
    /// The direct consequence of the operator's choice, and the thing most likely to be got wrong:
    /// the argument is optional, so the honest record is "nothing stated". It also proves the schema
    /// change is backward compatible — this is a two-argument `open`, exactly as every client that
    /// predates `callerModel` sends it. (gemini, Major.)
    /// </remarks>
    [Fact]
    public async Task AClientThatSendsNoModel_OpensAnyway_AndStatesNoModel()
    {
        var repo = await ARepository();
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"some-editor","version":"2"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var opened = await RoundTrip(server, OpenCall(repo), timeoutSeconds: 60);
            opened.RootElement.GetProperty("result").TryGetProperty("isError", out var failed)
                .Should().BeFalse($"a two-argument open is what every older client sends: {opened.RootElement}");
            failed.ValueKind.Should().Be(JsonValueKind.Undefined);

            var caller = OnlySessionFile().GetProperty("caller");
            caller.GetProperty("model").GetString().Should().BeEmpty("nobody declared one");
            caller.GetProperty("client").GetString().Should().Be("some-editor");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    /// <summary>A `tools/call` for `open`, with the model the caller declares — or none at all.</summary>
    /// <remarks>
    /// Composed rather than written as a raw literal: the JSON ends in `}}}`, which a `$$"""` cannot
    /// carry, and every fragment of it ends in a quote, which a `"""` cannot carry either.
    /// </remarks>
    private static string OpenCall(string repo, string declaredModel = "")
    {
        var arguments = new Dictionary<string, string> { ["repoPath"] = repo, ["branch"] = "main" };
        if (declaredModel.Length > 0)
        {
            arguments["callerModel"] = declaredModel;
        }

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["method"] = "tools/call",
            ["params"] = new Dictionary<string, object> { ["name"] = "open", ["arguments"] = arguments },
        });
    }

    /// <summary>The one session this server wrote, parsed.</summary>
    private JsonElement OnlySessionFile()
    {
        var files = Directory.GetFiles(Path.Combine(_data, "sessions"), "session-*.json");
        files.Should().ContainSingle("one open, one session");
        return JsonDocument.Parse(File.ReadAllText(files[0])).RootElement;
    }

    /// <summary>A real git checkout with one commit — `open` resolves a SHA before it does anything.</summary>
    private async Task<string> ARepository()
    {
        var repo = Directory.CreateTempSubdirectory("coai-contract-repo-").FullName;
        await File.WriteAllTextAsync(Path.Combine(repo, "app.cs"), "v1\n");
        foreach (var args in (string[][])[["init", "-b", "main"], ["add", "."], ["commit", "-m", "base"]])
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = repo,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            string[] all =
                ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args];
            foreach (var argument in all)
            {
                start.ArgumentList.Add(argument);
            }

            using var git = Process.Start(start)!;
            await git.WaitForExitAsync();
            git.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {await git.StandardError.ReadToEndAsync()}");
        }

        _repos.Add(repo);
        return repo.Replace('\\', '/');
    }

    private readonly List<string> _repos = [];
}
