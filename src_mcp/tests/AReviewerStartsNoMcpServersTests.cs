using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer and a consultant start NO MCP servers — issue #514.
/// </summary>
/// <remarks>
/// Each vendor CLI otherwise loads the person's own servers. For Claude that list held <c>coai</c> itself, so
/// every Claude reviewer started a serving coai-mcp and killed it on the way out, which the next start
/// reported as a run that "never finished"; for Codex it held an <c>npx</c> package started per reviewer.
/// </remarks>
public sealed class AReviewerStartsNoMcpServersTests
{
    private const string Repo = "D:/rsd/some-checkout";

    private static readonly string[] Configured = ["azure-devops", "two words", "it's"];

    private static IReadOnlyList<string> ClaudeReviewer() =>
        new ClaudeRuntime().Build(RoleCatalog.ArchitectureRole, "review this", Repo, "D:/s.json", "D:/out", new("claude"))
            .Request.Arguments;

    private static IReadOnlyList<string> ClaudeConsultant() =>
        new ClaudeConsultant(new ClaudeRuntime())
            .Build(new ConsultantLaunch(Repo, "help me", string.Empty, "D:/answers", new ReviewerSettings("claude")))
            .Request.Arguments;

    private static IReadOnlyList<string> CodexReviewer(IReadOnlyList<string> off) =>
        new CodexRuntime().Build(RoleCatalog.ArchitectureRole, "review this", Repo, "D:/s.json", "D:/out",
            new ReviewerSettings("codex") { McpServersToSwitchOff = off }).Request.Arguments;

    private static IReadOnlyList<string> CodexConsultant(IReadOnlyList<string> off, string handle = "") =>
        new CodexConsultant(new CodexRuntime())
            .Build(new ConsultantLaunch(Repo, "help me", handle, "D:/answers", new ReviewerSettings("codex") { McpServersToSwitchOff = off }))
            .Request.Arguments;

    /// <summary>The <c>-c</c> values a launch carries, in order.</summary>
    private static IEnumerable<string> Overrides(IReadOnlyList<string> args) =>
        args.Zip(args.Skip(1)).Where(pair => pair.First == "-c").Select(pair => pair.Second);

    private static readonly string[] SwitchedOff =
        ["mcp_servers.azure-devops.enabled=false", "mcp_servers.'two words'.enabled=false", "mcp_servers.\"it's\".enabled=false"];

    [Fact]
    public void AClaudeReviewer_AndAClaudeConsultant_TakeMcpServersOnlyFromAConfigTheyAreNeverGiven()
    {
        foreach (var (who, args) in new[] { ("reviewer", ClaudeReviewer()), ("consultant", ClaudeConsultant()) })
        {
            args.Should().Contain(NoMcpServers.ClaudeFlag, $"a Claude {who} loads the person's servers — coai itself among them — unless told not to");
            args.Should().NotContain("--mcp-config", $"and it is given no servers of its own, so a Claude {who} has none at all");
        }
    }

    [Fact]
    public void ACodexReviewer_SwitchesOffEveryServerTheMachineDeclares()
    {
        Overrides(CodexReviewer(Configured)).Should().Contain(SwitchedOff,
            "one override per declared server, each a valid TOML key — bare, 'literal', or \"basic\" when it holds a single quote");
        Overrides(CodexReviewer([])).Should().NotContain(o => o.StartsWith("mcp_servers.", StringComparison.Ordinal),
            "and a machine that declares none gets no overrides");
    }

    [Fact]
    public void ACodexConsultant_SwitchesThemOff_OnItsFirstTurn_AndWhenItResumes()
    {
        Overrides(CodexConsultant(Configured)).Should().Contain(SwitchedOff, "a first turn starts no MCP server");
        Overrides(CodexConsultant(Configured, handle: "0199a7c3-0000-7000-8000-000000000001")).Should().Contain(SwitchedOff,
            "and neither does a resumed one — a resume re-reads config.toml like any launch");
    }

    [Fact]
    public void EveryWayACodexConfigDeclaresAServer_IsFound_AndNothingElseIs()
    {
        const string toml = """
            model = "gpt-6-luna"
            # [mcp_servers.commented-out]
            [mcp_servers.azure-devops]
            command = "npx"
            args = ["-y", "a=b"]
            [mcp_servers.azure-devops.env]
            ADO_MCP_AUTH_TOKEN = "x"
            [mcp_servers."two words"]
            command = "x"
            [mcp_servers.'literal-name']
            command = "y"
            [mcp_servers]
            inline = { command = "z" }
            [projects.'d:\rsd\x']
            trust_level = "trusted"
            [tui]
            mcp_servers = "not a server table"
            """;

        NoMcpServers.CodexServerNames(toml).Should().Equal(["azure-devops", "two words", "literal-name", "inline"],
            "a sub-table is not a new server, a comment is nothing, and a key named mcp_servers inside another table is not the root one");
        NoMcpServers.CodexServerNames("mcp_servers.dotted.command = \"x\"\n").Should().Equal(["dotted"]);
        NoMcpServers.CodexServerNames("[mcp_servers.\"quote\\\"d\"]\n").Should().Equal(["quote\"d"]);
        NoMcpServers.CodexServerNames(string.Empty).Should().BeEmpty();
    }

    /// <summary>
    /// A name the reader is NOT sure of is never reported — measured on codex-cli 0.156.1, an override for
    /// a server config.toml does not declare stops codex from starting at all ("failed to load bootstrap
    /// configuration"). A missed name leaves one server loaded; a wrong one breaks every Codex review. (Our
    /// own code reviewer.)
    /// </summary>
    [Fact]
    public void ANameTheReaderIsNotSureOf_IsNeverReported_BecauseAWrongOneStopsCodex()
    {
        const string toml = """
            [mcp_servers]
            real = { command = "x" }
            notatable = 1
            [[plugins]]
            foo = 1
            mcp_servers.fake.command = "not at the root"
            """;

        NoMcpServers.CodexServerNames(toml).Should().Equal(["real"],
            "a plain value is not a server, an array table ends [mcp_servers], and a dotted key outside the root is not one either");
    }

    [Fact]
    public void ANameCmdWouldReadAsACommand_IsNotPassed()
    {
        Overrides(CodexReviewer(["ok", "srv&calc", "a|b", "per%cent"])).Should().Equal(["mcp_servers.ok.enabled=false"],
            "on Windows codex is an npm .cmd shim, and cmd.exe splits an unquoted & or | into a second command (gemini, the code round)");
    }

    [Fact]
    public void TheCodexConfigIsTheOneTheLaunchWillRead_HomeIncluded()
    {
        var home = Directory.CreateTempSubdirectory("coai-codex-home-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(home, ".codex"));
            File.WriteAllText(Path.Combine(home, ".codex", "config.toml"), "[mcp_servers.from-slot]\ncommand = \"x\"\n");

            NoMcpServers.CodexConfigured(name => name == "HOME" ? home : null).Should().Equal(["from-slot"],
                "on the Team server codex runs as a slot, with the slot's HOME — the config is that home's, not the server's");
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void AGeminiFamilyFile_NamesItsServers_OrNone()
    {
        NoMcpServers.JsonServerNames("""{"mcpServers":{"a":{},"b":{}}}""").Should().Equal(["a", "b"]);
        NoMcpServers.JsonServerNames("""{"mcpServers":{}}""").Should().BeEmpty();
        NoMcpServers.JsonServerNames("{}").Should().BeEmpty();
        NoMcpServers.JsonServerNames("not json").Should().BeEmpty("an unreadable file says nothing rather than failing a start");
    }
}
