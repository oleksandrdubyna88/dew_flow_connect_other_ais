using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A mode this binary KNOWS never answers 64, whatever is wrong with the request.
/// </summary>
/// <remarks>
/// <para><b>64 is how the extension detects an OLD server.</b> `.agents/PROJECT.md` fixes both halves
/// of that rule: a mode the binary does not have must exit 64 so the caller can fall back, and a mode
/// it DOES have must never exit 64 — because a request fault wearing that code sends the caller down
/// the fallback and hides the fault behind a successful-looking answer. `--findings-many` answers an
/// unreadable keys file with <b>65 (EX_DATAERR)</b> for exactly this reason.</para>
/// <para><b>`--collect-bugs` shipped answering 64 for a refused model.</b> A model that may not be
/// shown un-anonymised findings is a request fault — the binary understood the mode perfectly and
/// declined the argument — so it is 65. Nothing reads that difference today, because the panel shows
/// the message on any non-zero exit; it would begin to matter the moment anything learns to fall back
/// on 64, and by then the wrong code would be the older half of a shipped pair.</para>
/// <para>A plan round on 2026-09-15 raised this rule against `--bugs-json`, where it did not apply —
/// that mode parses no request document. The rule was right and its target was wrong, and the place
/// it DID apply went unnoticed until the story-5 plan round quoted the paragraph again.</para>
/// </remarks>
public sealed class ARequestFaultIsNotAnOldBinaryTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-exit-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string? _was = Environment.GetEnvironmentVariable("COAI_DATA_DIR");

    public ARequestFaultIsNotAnOldBinaryTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _dir);
    }

    /// <summary>A model that may not read findings is a bad ARGUMENT, not a missing mode.</summary>
    [Fact]
    public async Task ARefusedModelIsADataError_NotAnUnknownMode()
    {
        var code = await Program.CollectBugsAsync(["--collect-bugs", "--model", "gemini/pro"]);

        code.Should().Be(
            65,
            "64 means 'this binary is too old for that mode' and sends the caller down a fallback; "
            + "this binary knows the mode and is refusing the argument");
    }

    /// <summary>And the refusal still says why, in the collector's own words.</summary>
    /// <remarks>
    /// An exit code a person cannot act on is a number. The sentence naming the vendor and the reason
    /// is what makes the refusal a decision rather than a failure.
    /// </remarks>
    [Fact]
    public async Task TheRefusalSaysWhichModelAndWhy()
    {
        var stderr = new StringWriter();
        var was = Console.Error;
        try
        {
            Console.SetError(stderr);
            await Program.CollectBugsAsync(["--collect-bugs", "--model", "gemini/pro"]);
        }
        finally
        {
            Console.SetError(was);
        }

        stderr.ToString().Should().Contain("gemini/pro").And.Contain("not a local model");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("COAI_DATA_DIR", _was);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
