using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An MCP client's launch of <c>coai-mcp</c> (serve) never creates a NAMED data folder that is confirmed absent
/// (research/PLAN_names_and_a_missing_root.md, story 3.2). It used to make it at once — the file logger under
/// <c>&lt;data&gt;/logs</c>, then the run marker, the notices ledger, the start's sweeps and the consultants survey.
/// </summary>
/// <remarks>
/// <para><b>How it refuses — the decision the plan recorded, verified here first.</b> Exiting 74 would leave a client
/// that shows only "server failed". So serve branches, BEFORE the logger is built, into a refusal-only server: the stdio
/// protocol answers, the tool list is the real one, and every tool call is an MCP error naming the folder — "restore it,
/// then restart the MCP server", because this server does not watch for it to come back. No host, no notices, no run
/// marker, no sweep, no survey: a "disable the logger and refuse the tools" version would still have started those.</para>
/// <para>The real binary over real stdio (<see cref="StdioServer"/>), because what is asserted is what the PROCESS does
/// to the disk over time — so the test waits past a sweep's beat before it looks the last time.</para>
/// </remarks>
public sealed class TheServerRefusesAMissingNamedRootTests : IDisposable
{
    private readonly string _parent = Directory.CreateTempSubdirectory("coai-serve-missing-").FullName;

    private string Missing => Path.Combine(_parent, "named-but-missing");

    [Fact]
    public async Task Serve_AnswersTheProtocol_RefusesEveryToolNamingTheFolder_AndNeverMakesIt()
    {
        using var server = StdioServer.Start(Missing, "debug", 1800, ("COAI_SWEEP_SECONDS", "10"));
        try
        {
            await StdioServer.InitializeAsync(server);
            Directory.Exists(Missing).Should().BeFalse($"initialize made the missing data folder, holding: {Inside()}");

            var tools = await StdioServer.RoundTrip(server, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
            var names = tools.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()).ToList();
            names.Should().Contain(["providers", "open", "review_plan", "review_code"], "a client is shown the real tools, which then say why they refuse");

            // Past a sweep's beat (10 s, its floor) and the consultants survey's start: what a full server would have written by now.
            await Task.Delay(TimeSpan.FromSeconds(12), TestContext.Current.CancellationToken);
            Directory.Exists(Missing).Should().BeFalse($"the server made the missing data folder while serving, holding: {Inside()}");

            // Every tool goes through the refusing service source, not only one (CodeRabbit on #777): a read, the session
            // door and both gates.
            var calls = new[]
            {
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"providers","arguments":{}}}""",
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"open","arguments":{"repoPath":"repo","branch":"main"}}}""",
                """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"review_plan","arguments":{"repoPath":"repo","branch":"main","planText":"plan"}}}""",
                """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"review_code","arguments":{"repoPath":"repo","branch":"main","baseRef":"HEAD^","planText":"plan"}}}""",
            };

            foreach (var call in calls)
            {
                var answer = await StdioServer.RoundTrip(server, call);
                var result = answer.RootElement.GetProperty("result");
                var said = result.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;

                result.GetProperty("isError").GetBoolean().Should().BeTrue($"the call must be refused, not answered: {answer.RootElement}");
                said.Should().Contain(Missing, "the refusal names the folder a person has to restore")
                    .And.Contain("restore it, then restart the MCP server");
            }
            Directory.Exists(Missing).Should().BeFalse($"the refused call made the missing data folder, holding: {Inside()}");
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }

        Directory.Exists(Missing).Should().BeFalse($"the ending made the missing data folder, holding: {Inside()}");
    }

    /// <summary>The decision serve branches on: only a CONFIRMED absence; a root the disk will not describe is served.</summary>
    [Fact]
    public void ServeRefuses_OnlyAConfirmedAbsence()
    {
        Func<string, string?> named = name => name == "COAI_DATA_DIR" ? Missing : null;

        Program.ServeRefusal(named, File.GetAttributes).Should().Contain(Missing).And.EndWith("restore it, then restart the MCP server.");
        Program.ServeRefusal(named, path => throw new UnauthorizedAccessException(path)).Should().BeEmpty("cannot tell is not absent — a sleeping share is served");
        Program.ServeRefusal(named, _ => throw new IOException("busy")).Should().BeEmpty("cannot tell is not absent");
        Program.ServeRefusal(_ => null, path => throw new DirectoryNotFoundException(path)).Should().BeEmpty("the default location is made lazily");
    }

    private string Inside()
    {
        try
        {
            var found = Directory.EnumerateFileSystemEntries(Missing, "*", SearchOption.AllDirectories)
                .Select(entry => Path.GetRelativePath(Missing, entry));

            return string.Join(", ", found) is { Length: > 0 } listed ? listed : "(nothing)";
        }
        catch (DirectoryNotFoundException)
        {
            return "(not there)";
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_parent, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A child may still hold a file for a moment after the kill.
        }
    }
}
