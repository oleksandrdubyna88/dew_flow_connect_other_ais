using Xunit;
using CoaiMcp.ServiceDefaults;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>The path shape is the contract shared across the family — so it is a unit test.</summary>
public sealed class LogPathTests
{
    [Fact]
    public void Shape_IsDayFolderThenAppTimePid()
    {
        var path = CoaiLogPath.For("logs", "coai-mcp", new DateTime(2026, 8, 31, 13, 5, 9, DateTimeKind.Utc), 4242);

        path.Replace('\\', '/').Should().Be("logs/2026-08-31/coai-mcp-13-05-09-4242.log");
    }

    [Fact]
    public void TwoHostsInTheSameSecond_GetDistinctFiles_ByPid()
    {
        var now = new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc);

        CoaiLogPath.For("logs", "a", now, 1).Should().NotBe(CoaiLogPath.For("logs", "a", now, 2));
    }

    [Fact]
    public void TheLogLivesUnderTheDataDirectory_NotBesideTheBinary()
    {
        // Reported on 2026-09-06 as "the MCP server is dead". Answering it took a quarter of an hour,
        // and the answer was one line in a log file whose location had to be GUESSED: `logs/` beside
        // the binary, which for an installed extension is
        // …/globalStorage/<publisher>.<extension>/logs. The data directory is the one place a person
        // is told about - the sessions, settings.json and coai.db are already there.
        var root = CoaiLogPath.RootFor(Path.Combine("C:", "Users", "ada", "AppData", "Local", "coai-mcp"));

        root.Should().Be(Path.Combine("C:", "Users", "ada", "AppData", "Local", "coai-mcp", "logs"));
        CoaiLogPath.For(root, "coai-mcp", new DateTime(2026, 9, 6, 11, 48, 18, DateTimeKind.Utc), 18756)
            .Should().EndWith(Path.Combine("coai-mcp", "logs", "2026-09-06", "coai-mcp-11-48-18-18756.log"));
    }
}
