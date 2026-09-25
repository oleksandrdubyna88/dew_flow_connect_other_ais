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
/// <para><b>And `--close-consult` shipped its first draft answering 64 for missing arguments</b>,
/// which is the same defect with the fallback actually wired: the panel reads that code to decide
/// whether to tell a person their server is too old for the feature. A malformed request would have
/// sent somebody to update a server that was fine. Its own cases live with that mode's scenario
/// test, where the process is really run; this is where the RULE is written down.
/// (codex SecurityReliability, issue #309's code round.)</para>
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

    /// <summary>Every one-shot mode this binary HAS is named here, so a new one cannot be forgotten.</summary>
    /// <remarks>
    /// Read out of <c>.agents/PROJECT.md</c> rather than listed here, because the rule's own sentence
    /// — "Adding a one-shot mode means adding it here" — is what makes that file the list. A mode
    /// added to <c>Program.cs</c> and not to the document is caught by this, not by a reviewer.
    /// </remarks>
    [Fact]
    public void TheSanctionedModesAreTheOnesTheDocumentNames()
    {
        var document = Path.Combine(RepoRoot(), ".agents", "PROJECT.md");
        File.Exists(document).Should().BeTrue(document);

        var named = File.ReadAllText(document);

        // EVERY mode the binary actually has, read off the switch that selects them rather than
        // listed here a second time. A hand-kept list in a test is a third copy of the same fact,
        // and the third copy is the one nobody updates. The first version of this asserted ONE
        // flag — the one that story happened to add — which would have stayed green through every
        // mode added after it.
        foreach (var mode in Modes())
        {
            named.Should().Contain(
                mode,
                $"`{mode}` is a one-shot mode this binary HAS, and a mode the document does not name "
                + "is a mode whose exit-code contract nobody agreed to");
        }
    }

    /// <summary>The one-shot modes, taken from the switch that selects them.</summary>
    /// <remarks>
    /// <para>This project's own source, not another program's — the distinction
    /// <see cref="NothingReadsAnotherProgramsSourceTests"/> draws. And it is a POSITIVE read: it
    /// derives the list the document must contain, so a switch that stops matching fails loudly on
    /// the count below rather than deriving an empty list every assertion passes over.</para>
    /// </remarks>
    private static IReadOnlyList<string> Modes()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src_mcp", "src", "Program.cs"));
        var modes = System.Text.RegularExpressions.Regex
            .Matches(source, "\"(--[a-z-]+)\" => Startup\\.")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        // The guard the rule about source-reading tests asks for: a regular expression that stops
        // matching answers nothing, and nothing passes every assertion above in silence.
        modes.Should().HaveCountGreaterThan(10, "the switch that selects one-shot modes was not found");

        return modes;
    }

    /// <summary>Walks up to the repository root, which the test binary sits four folders under.</summary>
    private static string RepoRoot()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, ".agents")))
        {
            here = here.Parent;
        }

        return here?.FullName ?? AppContext.BaseDirectory;
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

    /// <summary>
    /// <c>--ask-local</c> without its arguments is a bad REQUEST: the binary knows the mode.
    /// </summary>
    /// <remarks>
    /// It answered 64 (§9.7 of the feature-review plan), which is the code that means "this binary
    /// has never heard of that mode" — so a caller that learns to fall back on 64 would have gone
    /// looking for an older shim because a prompt file was not named.
    /// </remarks>
    [Fact]
    public async Task AnAskLocalWithoutItsArguments_IsADataError_NotAnUnknownMode()
    {
        var code = await Program.AskLocalAsync(["--ask-local"]);

        code.Should().Be(65, "the mode is known and its request is malformed — 64 would read as an old binary");
    }

    /// <summary>
    /// <c>--normalize</c> without <c>--in</c>/<c>--out</c> is a bad REQUEST too: the binary knows the mode.
    /// </summary>
    /// <remarks>
    /// The same defect as <c>--ask-local</c>'s, found beside it while the feature-review plan's §9.7 was
    /// being fixed: it answered 64, which a caller reads as "this binary is older than that mode".
    /// </remarks>
    [Fact]
    public void ANormalizeWithoutItsArguments_IsADataError_NotAnUnknownMode()
    {
        var code = Program.NormalizeJson(["--normalize"]);

        code.Should().Be(65, "the mode is known and its request is malformed — 64 would read as an old binary");
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
