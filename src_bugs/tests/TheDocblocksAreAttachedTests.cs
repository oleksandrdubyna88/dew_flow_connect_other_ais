using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// No documentation comment is stranded from the member it describes.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Inserting a method between an existing doc comment and the member
/// it belonged to leaves TWO complete blocks in a row: the new member gets the old member's summary,
/// and the old member gets none. Nothing notices — it compiles, the analysers are happy, and the
/// only symptom is a reader being told something untrue about the code they are looking at. It
/// happened FOUR times in one story: `Corpus.AuditTrail` lost its summary to `AcceptAdmin`,
/// `RateLimiter.StampsOf` lost its to `ActiveNow`, `Corpus.Revoke` kept its superseded block above
/// the new one, and `Corpus.KeysPage`'s paging rationale was stranded above an overload added later.
/// Three of those were found by eye, which is exactly the argument for a test.</para>
/// <para><b>What it actually checks.</b> A closing <c>&lt;/summary&gt;</c>,
/// <c>&lt;/remarks&gt;</c> or <c>&lt;/param&gt;</c> line followed immediately by an opening
/// <c>&lt;summary&gt;</c> line. That is the shape of two blocks in a row and it has no legitimate
/// use: one member has one summary, and a second summary either belongs to something else or is a
/// leftover. It is deliberately NOT a check that every public member is documented — that is the
/// analysers' job and they already do it.</para>
/// </remarks>
public sealed class TheDocblocksAreAttachedTests
{
    [Fact]
    public void NoMemberCarriesTwoDocumentationBlocks()
    {
        var stranded = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Source(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                // Generated code and build output are nobody's to keep tidy.
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var at = 1; at < lines.Length; at++)
            {
                if (Closes(lines[at - 1]) && Opens(lines[at]))
                {
                    stranded.Add($"{Path.GetFileName(file)}:{at + 1}");
                }
            }
        }

        stranded.Should().BeEmpty(
            "a second documentation block in a row means one member is described by a comment "
            + "written for another: either a doc comment was stranded by a member inserted below it, "
            + "or a superseded block was left above its replacement");
    }

    /// <summary>A documentation line that ENDS a block, on one line or many.</summary>
    /// <remarks>
    /// It matched only a closing tag alone on its line, and this codebase writes most summaries on
    /// ONE line — so the first version of this guard missed the very case it was written for, the
    /// stranded single-line summary above `RateLimiter.StampsOf`. Proved by re-creating that shape
    /// and watching the test stay green, which is why the break-it step is not optional.
    /// </remarks>
    private static bool Closes(string line) =>
        line.TrimStart() is { } text
        && text.StartsWith("///", StringComparison.Ordinal)
        && (text.EndsWith("</summary>", StringComparison.Ordinal)
            || text.EndsWith("</remarks>", StringComparison.Ordinal)
            || text.EndsWith("</param>", StringComparison.Ordinal));

    private static bool Opens(string line) => line.TrimStart().StartsWith("/// <summary>", StringComparison.Ordinal);

    /// <summary>Both source trees of this server, found from the test binary.</summary>
    private static string Source()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src_bugs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("no src_bugs above the test binary");
    }
}
