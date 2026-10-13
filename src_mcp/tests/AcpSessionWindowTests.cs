using System.Text.Json;
using CoaiMcp.Runners.Acp;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The agent's optional <c>line</c> / <c>limit</c> on <c>fs/read_text_file</c> (<see cref="AcpSession.Window"/>): the
/// same window the first version cut, without splitting the whole file to cut it.
/// </summary>
public sealed class AcpSessionWindowTests
{
    private static JsonElement Params(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>The first version, kept as the reference the window must still agree with.</summary>
    private static string ByASplit(string text, long line, long limit)
    {
        if (line <= 1 && limit <= 0)
        {
            return text;
        }

        var lines = text.Split('\n');
        var from = (int)Math.Clamp(line - 1, 0, lines.Length);
        var count = limit > 0 ? (int)Math.Min(limit, lines.Length - from) : lines.Length - from;

        return string.Join('\n', lines.Skip(from).Take(count));
    }

    [Theory]
    [InlineData("a\nb\nc", 0, 0)]
    [InlineData("a\nb\nc", 1, 0)]
    [InlineData("a\nb\nc", 2, 0)]
    [InlineData("a\nb\nc", 2, 1)]
    [InlineData("a\nb\nc", 3, 5)]
    [InlineData("a\nb\nc", 4, 0)]
    [InlineData("a\nb\nc", 9, 2)]
    [InlineData("a\nb\nc\n", 3, 0)]
    [InlineData("a\nb\nc\n", 4, 1)]
    [InlineData("a\nb\nc\n", 5, 1)]
    [InlineData("a\r\nb\r\nc", 2, 1)]
    [InlineData("a\nb\nc", -3, 2)]
    [InlineData("a\nb\nc", 0, 2)]
    [InlineData("", 1, 1)]
    [InlineData("", 2, 0)]
    [InlineData("only", 1, 1)]
    public void TheWindow_IsTheOneTheSplitCut(string text, long line, long limit)
    {
        var parameters = Params($$"""{"line": {{line}}, "limit": {{limit}}}""");

        AcpSession.Window(text, parameters).Should().Be(ByASplit(text, line, limit), $"line {line}, limit {limit}");
    }

    /// <summary>
    /// The code gate of epic 4 (2026-10-12, codex, UX-DX and performance): a window of two lines from a served file of
    /// two million short lines split the whole file first — an array and a string per line, tens of megabytes for
    /// two lines. The window is cut by scanning for its newlines; what is allocated is the window.
    /// </summary>
    [Fact]
    public void ATwoLineWindow_OfAFileOfMillionsOfLines_AllocatesTheWindowNotTheFile()
    {
        var text = string.Concat(Enumerable.Repeat("a\n", 2_000_000));
        var parameters = Params("""{"line": 5, "limit": 2}""");
        AcpSession.Window("warm\nup", parameters);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var window = AcpSession.Window(text, parameters);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        window.Should().Be("a\na");
        allocated.Should().BeLessThan(64 * 1024, "only the two-line window is new; the file is not split");
    }
}
