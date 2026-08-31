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
}
