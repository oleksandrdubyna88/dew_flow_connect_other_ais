using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The promise this server makes cannot outlive the schema it describes.
/// </summary>
/// <remarks>
/// <para><b>The shipped wording was "`submissions` is a counter without a clock".</b> It was true and
/// it was the whole reason the key table had no timestamp — with one, the corpus would be a record
/// of when a person we handed a key to was working. Then story 2 added <c>last_seen_month</c>,
/// because an administrator looking at a list of keys cannot otherwise tell a live one from an
/// abandoned one, and the sentence quietly became false in four places at once.</para>
/// <para><b>A promise nobody checks is a promise that drifts.</b> Prose is not compiled, so the only
/// thing that can notice is a test — and this one is deliberately CONDITIONAL rather than a flat
/// ban: the phrase is refused only while the column exists. Remove <c>last_seen_month</c> and the old
/// sentence becomes true again, and this test stops objecting to it, which is what makes it a
/// statement about the SYSTEM rather than a banned word list.</para>
/// <para><b>What it does not scan, and why.</b> `research/PLAN_*.md` are records of what shipped when
/// they shipped: a plan that said "a counter without a clock" in July was right in July, and a test
/// that rewrote it would be a test that deleted the reason the column was controversial. `todo/`
/// holds work not yet done, including the plan that ordered this test. Both are history or intent;
/// the live surfaces are the claim.</para>
/// </remarks>
public sealed class ThePromiseMatchesTheSchemaTests
{
    /// <summary>The sentence that was true before the column existed.</summary>
    private const string TheOldPromise = "counter without a clock";

    /// <summary>The column that made it false.</summary>
    private const string TheClock = "last_seen_month";

    /// <summary>Whether the key table still has a clock on it — the condition the ban rests on.</summary>
    private static bool TheKeyTableHasAClock() =>
        File.ReadAllText(Path.Combine(Root(), "src_bugs", "src", "CorpusSchema.cs"))
            .Contains($"ADD COLUMN {TheClock}", StringComparison.Ordinal);

    /// <summary>
    /// The scan still FINDS the sentence where it legitimately survives.
    /// </summary>
    /// <remarks>
    /// The repository's rule for a structural scan: the prohibition, and one assertion that the
    /// pattern still matches a known instance. Without it, a reformatting or a typo in
    /// <see cref="TheOldPromise"/> makes the prohibition vacuously green for ever, which is the
    /// failure mode of every "assert this text is absent" test ever written. The known instance is a
    /// plan in `research/`, where the sentence is a RECORD of what shipped and is meant to stay.
    /// (Code round, codex.)
    /// </remarks>
    [Fact]
    public void TheScanStillFindsTheSentenceWhereItIsSupposedToSurvive()
    {
        var record = Path.Combine(Root(), "research", "PLAN_the_bugs_release_line.md");

        File.Exists(record).Should().BeTrue("the known instance has to exist for this to prove anything");
        File.ReadAllText(record).Should().Contain(
            TheOldPromise,
            "if the pattern no longer matches here, the prohibition above is matching nothing anywhere");
    }

    /// <summary>
    /// And so nothing that describes this server may still say there is none.
    /// </summary>
    /// <remarks>
    /// ONE test over every file rather than a theory case per file: the useful failure names every
    /// place the sentence survives at once — it was in four when the column landed — and a hundred
    /// and twenty green cases per run to say "no drift" is noise, not evidence.
    /// </remarks>
    [Fact]
    public void NoLiveSurfaceSaysThereIsNoClock()
    {
        // CONDITIONAL, and this is the whole design: remove the column and the old sentence becomes
        // true again, so the ban lifts instead of standing in the way of a legitimate change.
        // (Code round, codex — the first version asserted the column's existence unconditionally,
        // which would have failed on exactly that change.)
        if (!TheKeyTableHasAClock())
        {
            return;
        }

        var saying = LiveFiles()
            .Where(file => File.ReadAllText(file).Contains(TheOldPromise, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Root(), file))
            .ToArray();

        saying.Should().BeEmpty(
            $"every one of these describes a key table that has {TheClock} on it. The promise as it "
            + "stands: submissions is a lifetime COUNT rather than a log of events, and the only "
            + "clock is a MONTH — never a date and never a time of day");
    }

    /// <summary>A scan that found nothing would pass every assertion above it.</summary>
    [Fact]
    public void TheScanActuallyReachesTheFilesItClaimsTo()
    {
        var files = LiveFiles();

        files.Should().HaveCountGreaterThan(20, "a scan of nothing proves nothing");
        files.Should().Contain(file => Path.GetFileName(file) == "coai-bugs", "the vhost carries most of the promise");
        files.Should().Contain(file => Path.GetFileName(file) == "Ingest.cs");
        files.Should().Contain(file => Path.GetFileName(file).StartsWith("module_", StringComparison.Ordinal));
        files.Should().NotContain(file => file.Contains("PLAN_", StringComparison.Ordinal), "history is not a claim");
    }

    /// <summary>Every file that describes this server as it is now.</summary>
    private static IReadOnlyList<string> LiveFiles()
    {
        var root = Root();
        string[][] where =
        [
            [root, "src_bugs", "src"],
            [root, "src_bugs", "tests"],
            [root, "deploy"],
            [root, "research"],
        ];

        return
        [
            .. where
                .Select(parts => Path.Combine(parts))
                .Where(Directory.Exists)
                .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                .Where(Prose)
                .Where(Live)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Whether a file is one somebody could have written the sentence in.
    /// </summary>
    /// <remarks>
    /// The vhost has no extension, which is why this is a list of names as well as suffixes. Reading
    /// every byte under four directories worked and does not scale: a fixture or an image dropped
    /// into `research/` would be loaded into memory on every run. (Code round, gemini.)
    /// </remarks>
    private static bool Prose(string file) =>
        Path.GetExtension(file) is ".cs" or ".md" or ".sh" or ".yml" or ".json" or ".sql"
            || Path.GetFileName(file) == "coai-bugs";

    /// <summary>Whether a file is a claim about the system rather than a record or a build artefact.</summary>
    private static bool Live(string file)
    {
        var name = Path.GetFileName(file);
        var inBuild = file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

        // And not THIS file, which has to quote the sentence in order to ban it. Excluded by name
        // rather than by assembling the phrase from pieces: a reader of a test about wording should
        // be able to see the wording.
        return !inBuild
            && name != "ThePromiseMatchesTheSchemaTests.cs"
            && !name.StartsWith("PLAN_", StringComparison.Ordinal)
            && !name.StartsWith("RESULTS", StringComparison.Ordinal);
    }

    /// <summary>The repository root, found from the test binary.</summary>
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src_bugs")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("no src_bugs above the test binary");
    }
}
