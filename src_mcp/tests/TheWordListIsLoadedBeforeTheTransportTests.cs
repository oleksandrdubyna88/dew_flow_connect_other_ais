using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>CredentialWords.EnsureLoaded()</c> runs before the transport is opened — and NOT before
/// <c>args[0]</c> is read.
/// </summary>
/// <remarks>
/// <para><b>Why it is where it is.</b> The published Native-AOT artefact must be exercised: a build
/// that dropped the embedded word list would otherwise redact with an empty list on somebody's
/// machine, silently, for ever. The first draft put the trigger as the first statement of
/// <c>Main</c>, and a reviewer refused it: <c>.agents/PROJECT.md</c> makes the one-shot CLI shape a
/// non-negotiable — a mode selected by <c>args[0]</c> BEFORE any transport is opened, answering and
/// exiting — and a startup obligation in front of <c>args[0]</c> makes <c>--version</c> and
/// <c>--help</c> depend on a word list they do not use and must not be able to fail on. So it is the
/// first statement of <c>ServeAsync</c>: before the logger, before the settings, before the
/// transport.</para>
/// <para><b>This test reads SOURCE, and the real check is the release smoke.</b> A structural scan
/// can only pin where the call is; that the published binary refuses without its list is proved by
/// <c>release.yml</c>, which runs <c>--version</c> AND a real <c>initialize</c> over stdio against the
/// published artefact — the second goes through <c>ServeAsync</c>, so a build missing the resource
/// fails the release loudly. What is pinned here is the WHOLE condition, not a fragment: exactly one
/// code occurrence, inside <c>ServeAsync</c>, before any other statement of it, before the
/// transport, and none inside <c>Main</c>. A fragment match — "the file contains the call" — would
/// survive the call moving back into <c>Main</c>.</para>
/// </remarks>
public sealed class TheWordListIsLoadedBeforeTheTransportTests
{
    private const string Call = "CredentialWords.EnsureLoaded();";

    private static readonly string[] Program = File.ReadAllLines(Path.Combine(
        NoSourceFileCarriesAControlByteTests.RepositoryRoot(), "src_mcp", "src", "Program.cs"));

    [Fact]
    public void EnsureLoaded_IsTheFirstStatementOfServeAsync_AndAppearsNowhereElse()
    {
        var serve = Body("private static async Task<int> ServeAsync()");
        var main = Body("private static async Task<int> Main(string[] args)");
        var calls = Program.Select((line, at) => (line, at))
            .Where(x => IsCode(x.line) && x.line.Contains(Call, StringComparison.Ordinal))
            .Select(x => x.at)
            .ToList();

        calls.Should().ContainSingle("the trigger runs once, in the one place a transport is opened");
        var at = calls[0];
        at.Should().NotBeInRange(main.Start, main.End,
            "in Main it would run before args[0], making --version and --help depend on the word list");
        at.Should().BeInRange(serve.Start, serve.End, "ServeAsync is the only path that opens a transport");
        Program[serve.Start..at].Where(IsCode).Should().BeEmpty(
            "it is the FIRST statement of ServeAsync — before the logger, the settings and the transport");
        var transport = Program.Select((line, i) => (line, i))
            .First(x => x.i > serve.Start && x.line.Contains("new StdioServerTransport(", StringComparison.Ordinal)).i;
        at.Should().BeLessThan(transport);
    }

    [Fact]
    public void TheScanFindsBothMethodsAndTheTransport_SoItCannotPassOverNothing()
    {
        var serve = Body("private static async Task<int> ServeAsync()");
        var main = Body("private static async Task<int> Main(string[] args)");

        serve.End.Should().BeGreaterThan(serve.Start);
        main.End.Should().BeGreaterThan(main.Start);
        Program[serve.Start..serve.End].Should().Contain(line => line.Contains("new StdioServerTransport(", StringComparison.Ordinal),
            "ServeAsync is where the transport is opened, or this test is scanning the wrong method");
        Program[main.Start..main.End].Should().Contain(line => line.Contains("Classify(args)", StringComparison.Ordinal),
            "Main is where args[0] is read, or this test is scanning the wrong method");
    }

    /// <summary>The line range of a method's body: after its opening brace, up to its closing brace at the class's member indent.</summary>
    private static (int Start, int End) Body(string signature)
    {
        var at = Array.FindIndex(Program, line => line.Trim() == signature);
        at.Should().BeGreaterThanOrEqualTo(0, $"Program.cs declares `{signature}`");
        var end = Array.FindIndex(Program, at, line => line == "    }");
        end.Should().BeGreaterThan(at, "the method body closes at the member indent");

        return (at + 2, end);
    }

    private static bool IsCode(string line) =>
        line.TrimStart() is { Length: > 0 } code && !code.StartsWith("//", StringComparison.Ordinal);
}
