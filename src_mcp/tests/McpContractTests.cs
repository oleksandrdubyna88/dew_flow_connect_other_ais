using System.Diagnostics;
using System.Text.Json;
using Xunit;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The wire itself: a real <c>coai-mcp</c> process, real stdio, real JSON-RPC. What these prove
/// no in-process test can — the handshake works, the twelve tools are advertised, and stdout
/// carries nothing but protocol.
/// </summary>
[Collection("fakecli-env")] // the server child inherits our env; keep FAKECLI_* quiet around it
public sealed class McpContractTests : IDisposable
{
    private static string ServerExe => ServerBinary.Path;

    private readonly string _data = TempDir.For("coai-contract-").Path;

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

    /// <summary>The real binary over real stdio — <see cref="StdioServer"/>, shared with the scenario suites since S3.</summary>
    private Process Start(
        string logLevel = "debug",
        int escalationSeconds = 1800,
        params (string Name, string Value)[] alsoInTheEnvironment) =>
        StdioServer.Start(_data, logLevel, escalationSeconds, alsoInTheEnvironment);

    private static Task<JsonDocument> RoundTrip(Process server, string request, int timeoutSeconds = 30) =>
        StdioServer.RoundTrip(server, request, timeoutSeconds);

    [Fact]
    public async Task Initialize_ThenToolsList_NamesTheThirteenTools_AndStdoutStaysPure()
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
                 "status", "ask_human", "cons", "cons_close", "consask", "roles"],
                "the thirteen tools, unprefixed — the client's `coai` namespace is the only one — with the renamed three under their v2 names and the v1 names not listed");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task ToolsList_PublishesReviewPlansPlanArguments_AsOptional_AndNoLongerClaimsACheckout()
    {
        // E1.S5: `planText` became one of two ways to name the plan, so it cannot be required; and the description said a
        // reviewer reads "a read-only checkout", which the plan stage stopped giving. Over the wire, because the schema
        // is built by the SDK from the C# signature — a default value is what makes an argument optional.
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"contract-test","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var tools = await RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var plan = tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Single(t => t.GetProperty("name").GetString() == "review_plan");

            var schema = plan.GetProperty("inputSchema");
            schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).Should().BeEquivalentTo(["repoPath", "branch"]);
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name)
                .Should().Contain(["planText", "committedPlan", "expectedCommit"]);
            var description = plan.GetProperty("description").GetString()!;
            description.Should().Contain("committedPlan").And.Contain("expectedCommit").And.Contain("END OF PLAN");
            description.Should().Contain("when the checkout is on that commit", "the edited-or-staged refusal only applies there");
            description.Should().NotContain("read-only checkout");
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
    /// options object the transport never sees. A client lists this as `/mcp__coai__cons`.</para>
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

            prompts.Should().BeEquivalentTo(["cons"], "one prompt, unprefixed, like the tools beside it, and the v1 name `consult` not listed");

            var got = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":3,"method":"prompts/get","params":{"name":"cons","arguments":{"problem":"The parser returns 3 where 4 is expected."}}}
                """);
            var message = got.RootElement.GetProperty("result").GetProperty("messages")[0];

            message.GetProperty("role").GetString().Should().Be("user");
            var text = message.GetProperty("content").GetProperty("text").GetString()!;
            text.Should().Contain("The parser returns 3 where 4 is expected.", "the person's own words travel unrewritten")
                .And.Contain("`cons` tool")
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
                    $$$"""{"jsonrpc":"2.0","id":4,"method":"prompts/get","params":{"name":"cons","arguments":{{{arguments}}}}}""")).RootElement;

                bare.TryGetProperty("error", out _).Should().BeFalse($"arguments {arguments} must be accepted: {bare}");
                bare.GetProperty("result").GetProperty("messages")[0]
                    .GetProperty("content").GetProperty("text").GetString()
                    .Should().Contain("expected to know it");
            }

            // A problem carrying punctuation and a code span travels unrewritten, which is the claim
            // the message makes about the person's words.
            var awkward = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":5,"method":"prompts/get","params":{"name":"cons","arguments":{"problem":"`Count()` returns 3 after two fixes; expected 4."}}}
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
    /// The v1 names are hidden aliases (v2 E3.S3 t1): each still ANSWERS — over the real wire, with the same reply the new name gives
    /// plus a `note` saying what it was renamed to — and none of them is LISTED, so a new client sees the new names only. The
    /// prompt <c>consult</c> is served the same way.
    /// </summary>
    [Theory]
    [InlineData("consult", "cons", """{"repoPath":"D:/nowhere","problem":"stuck"}""")]
    [InlineData("close_consult", "cons_close", """{"repoPath":"D:/nowhere","consultationId":"c1","outcome":"solved"}""")]
    [InlineData("ask_consultants", "consask", """{"repoPath":"D:/nowhere","question":"A or B?","context":"tried both"}""")]
    public async Task AnOldToolName_StillAnswers_LikeTheNewOne_WithANoteSayingItWasRenamed(string oldName, string newName, string arguments)
    {
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var listed = await RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var names = listed.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(t => t.GetProperty("name").GetString()).ToList();
            names.Should().Contain(newName).And.NotContain(oldName, "a hidden alias is callable and not listed");

            string Call(int id, string name) =>
                $$$"""{"jsonrpc":"2.0","id":{{{id}}},"method":"tools/call","params":{"name":"{{{name}}}","arguments":{{{arguments}}}}}""";
            var viaNew = JsonDocument.Parse((await RoundTrip(server, Call(3, newName), 30)).RootElement
                .GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
            var viaOld = JsonDocument.Parse((await RoundTrip(server, Call(4, oldName), 30)).RootElement
                .GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;

            viaOld.GetProperty("note").GetString().Should().StartWith($"renamed to {newName}");
            viaNew.TryGetProperty("note", out _).Should().BeFalse("the new name carries no note");
            viaOld.EnumerateObject().Where(p => p.Name != "note").Select(p => p.Name)
                .Should().Equal(viaNew.EnumerateObject().Select(p => p.Name), "the old name answers with the new one's members, in its order");
            viaOld.EnumerateObject().Where(p => p.Name != "note" && p.Value.ValueKind is JsonValueKind.String)
                .Select(p => (p.Name, Value: p.Value.GetString()))
                .Should().Equal(viaNew.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.String).Select(p => (p.Name, Value: p.Value.GetString())),
                    "and the same words");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    [Fact]
    public async Task TheOldPromptName_StillGivesTheMessage_ButIsNotListed()
    {
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var old = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":2,"method":"prompts/get","params":{"name":"consult","arguments":{"problem":"red twice"}}}
                """);
            var viaNew = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":3,"method":"prompts/get","params":{"name":"cons","arguments":{"problem":"red twice"}}}
                """);

            Text(old).Should().Be(Text(viaNew), "the old prompt hands out the same message");
            Text(old).Should().Contain("red twice").And.Contain("`cons` tool");

            static string Text(JsonDocument document) =>
                document.RootElement.GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString()!;
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
                .Contain("`cons` is for when YOU are stuck", "the tool has to be named for a client that lists no prompts")
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
    /// <c>ask_human</c> over the real wire in v2: the tool stays registered for old callers, no card is written and nothing waits — the
    /// reply is the one JSON object that tells the caller to ask in its own conversation.
    /// </summary>
    [Fact]
    public async Task AskHuman_IsAnAlias_AnswersAtOnceToAskInTheConversation_WhateverTheArguments()
    {
        using var server = Start(escalationSeconds: 60);
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var answer = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"ask_human","arguments":{"repoPath":"D:/nowhere","branch":"main","question":"A or B?"}}}
                """, timeoutSeconds: 20);
            var text = answer.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();

            var reply = JsonDocument.Parse(text!).RootElement;
            reply.GetProperty("status").GetString().Should().Be("ask_in_conversation", "the wait is 60 s and the read 20 s: only an answer that did not wait arrives");
            reply.GetProperty("note").GetString().Should().Be("removed in v2: coai never waits for a person; ask in your own conversation");
            reply.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["status", "note"], "nothing else rides on it");
            Directory.Exists(Path.Combine(_data, "escalations")).Should().BeFalse("no card was written");
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
    /// An old caller still sends <c>humanDecision: proceed</c>: v2's <c>resolve</c> does not declare it, the SDK drops an undeclared
    /// argument without an error, and the answer is our own sentence — not an invocation failure, and no release of anything.
    /// </summary>
    [Fact]
    public async Task Resolve_FromAnOldCaller_WithHumanDecisionProceed_IsAnAnswer_NotAnSdkError()
    {
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"contract-test","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var answer = await RoundTrip(server, """
                {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"resolve","arguments":{"repoPath":"D:/nowhere","branch":"main","decisions":"[]","humanDecision":"proceed"}}}
                """);

            var text = answer.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
            JsonDocument.Parse(text!).RootElement.GetProperty("error").GetString().Should().Contain("call open first");
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

    /// <summary>
    /// A `head` that is not the checkout's HEAD is REFUSED over the wire, naming both commits — and one that
    /// is, proceeds.
    /// </summary>
    /// <remarks>
    /// <para>Over the WIRE, because that is where the defect lived (§9.30 of the feature-review plan): the
    /// snippet told callers to pass `head`, the tool declared none, and the SDK drops an undeclared argument
    /// without an error — so a caller naming another commit got a clean review of whatever the checkout
    /// held. No in-process test can see an argument the SDK threw away before the service was called.</para>
    /// <para>Two epics, below the D17 line, so the call is a recorded `skipped` and no reviewer can launch
    /// whatever this machine's vendors are: the head is checked before that skip, which is the point.</para>
    /// </remarks>
    [Fact]
    public async Task AHeadThatIsNotTheCheckoutsHead_IsRefusedOverTheWire_AndTheCheckoutsOwnHeadProceeds()
    {
        var (repo, baseSha, headSha) = await AFeatureRepository();
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"contract-test","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var other = await FeatureAnswer(server, FeatureCall(repo, baseSha, head: baseSha));
            other.TryGetProperty("error", out var sentence).Should().BeTrue(
                $"a head other than the checkout's must be refused, not reviewed as the checkout: {other}");
            sentence.GetString().Should().Contain(baseSha, "the refusal names the commit the caller named")
                .And.Contain(headSha, "and the commit the checkout actually holds");

            var same = await FeatureAnswer(server, FeatureCall(repo, baseSha, head: headSha, id: 3));
            same.TryGetProperty("error", out var refused).Should().BeFalse($"the checkout's own head proceeds: {refused}");
            same.GetProperty("verdict").GetString().Should().Be("skipped", "two epics are under the D17 line");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    /// <summary>`head` is declared, and OPTIONAL — every client that never sent one keeps calling as it did.</summary>
    [Fact]
    public async Task ReviewFeature_DeclaresHead_AsAnOptionalArgument()
    {
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"contract-test","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var tools = await RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var schema = tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Single(t => t.GetProperty("name").GetString() == "review_feature")
                .GetProperty("inputSchema");

            schema.GetProperty("properties").TryGetProperty("head", out _).Should().BeTrue(
                "an undeclared argument is dropped by the SDK without an error, which is how a wrong head went unnoticed");
            schema.GetProperty("required").EnumerateArray().Select(r => r.GetString())
                .Should().NotContain("head", "a call without it keeps its old meaning — the checkout's HEAD");
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    /// <summary>
    /// `consultId`, `productionRisk` and `riskReason` are declared on `ask_human`, and OPTIONAL — every client that
    /// never sent one keeps calling as it did (<c>todo/PLAN_question_consultant.md</c>, S3 acceptance 6).
    /// </summary>
    [Fact]
    public async Task AskHuman_DeclaresItsThreeGateArguments_AllOptional()
    {
        using var server = Start();
        try
        {
            await RoundTrip(server, """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"contract-test","version":"0"}}}
                """);
            await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await server.StandardInput.FlushAsync();

            var tools = await RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var schema = tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Single(t => t.GetProperty("name").GetString() == "ask_human")
                .GetProperty("inputSchema");
            var properties = schema.GetProperty("properties");
            var required = schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ToList();

            foreach (var argument in new[] { "consultId", "productionRisk", "riskReason" })
            {
                properties.TryGetProperty(argument, out _).Should().BeTrue(
                    $"`{argument}` is read by the gate, and an undeclared argument is dropped by the SDK without an error");
                required.Should().NotContain(argument, "a call without it keeps its old meaning — a plain question");
            }
        }
        finally
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
        }
    }

    /// <summary>The parsed answer of one `review_feature` call.</summary>
    private static async Task<JsonElement> FeatureAnswer(Process server, string call)
    {
        var answer = await RoundTrip(server, call, timeoutSeconds: 60);
        answer.RootElement.TryGetProperty("result", out var result)
            .Should().BeTrue($"the call must be answered by the tool, not by the SDK: {answer.RootElement}");

        return JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
    }

    /// <summary>A `tools/call` for `review_feature` over two epics, with the head the caller names.</summary>
    private static string FeatureCall(string repo, string baseSha, string head, int id = 2)
    {
        var arguments = new Dictionary<string, string>
        {
            ["repoPath"] = repo,
            ["planPath"] = "todo/PLAN_x.md",
            ["baseRef"] = baseSha,
            ["head"] = head,
            ["epics"] = """[{"title": "One", "summary": "The first epic."}, {"title": "Two", "summary": "The second epic."}]""",
            ["lessons"] = """
                {"pitfalls": ["The parser read a trailing comma as an empty entry until epic 2 fixed the tokenizer."],
                 "blockers": ["none — nothing blocked; the one open question was the flag name and it was settled in review."],
                 "findings": ["Epic 2 relies on the tokenizer fix from epic 1; a reviewer of the whole should check the seam."]}
                """,
        };

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = "tools/call",
            ["params"] = new Dictionary<string, object> { ["name"] = "review_feature", ["arguments"] = arguments },
        });
    }

    /// <summary>A checkout with a plan and two commits — the base, and the head the checkout holds.</summary>
    private async Task<(string Repo, string Base, string Head)> AFeatureRepository()
    {
        var repo = TempDir.For("coai-contract-feature-").Path;
        _repos.Add(repo);
        Directory.CreateDirectory(Path.Combine(repo, "todo"));
        await File.WriteAllTextAsync(Path.Combine(repo, "todo", "PLAN_x.md"), "# PLAN — x\n\nTwo epics.\n");
        await File.WriteAllTextAsync(Path.Combine(repo, "app.cs"), "v1\n");
        await GitIn(repo, "init", "-b", "main");
        await GitIn(repo, "add", ".");
        await GitIn(repo, "commit", "-m", "base");
        var baseSha = (await GitIn(repo, "rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo, "app.cs"), "v2\n");
        await GitIn(repo, "commit", "-am", "the feature");
        var headSha = (await GitIn(repo, "rev-parse", "HEAD")).Trim();

        return (repo.Replace('\\', '/'), baseSha, headSha);
    }

    private static async Task<string> GitIn(string repo, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in (string[])["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args])
        {
            start.ArgumentList.Add(argument);
        }

        using var git = Process.Start(start)!;
        var output = await git.StandardOutput.ReadToEndAsync();
        await git.WaitForExitAsync();
        git.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {await git.StandardError.ReadToEndAsync()}");

        return output;
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
        var repo = TempDir.For("coai-contract-repo-").Path;
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
