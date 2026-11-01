using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Resolving a path through links is ONE road — <c>RealPath</c> — and every other reader of a link says why it is
/// not a containment decision.
/// </summary>
/// <remarks>
/// <para><b>The class, counted mechanically (2026-11-01).</b> The same hand-written walk, taking
/// <c>ResolveLinkTarget(…).FullName</c> for a link, stood at four sites: the document reader, the worktree
/// deletion guard, the worktree path comparison and the guard's own resolver. A walk like that never resolves the
/// directories INSIDE a link's target and folds a <c>..</c> in link text lexically. This scan is what was written
/// BEFORE the fix, per <c>common/security.md</c>: its first failure is the list of sites.</para>
/// <para>Every remaining reader of <c>.LinkTarget</c> is listed with its reason. A new one fails this test and has
/// to be added here — with a reason that it decides nothing about containment — or to use the shared resolver.</para>
/// </remarks>
public sealed class TheLinkResolverIsOneRoadTests
{
    private const string TheResolver = "src_mcp/runners/Files/RealPath.cs";

    /// <summary>Readers of the raw link text, and why each is not a containment decision.</summary>
    private static readonly Dictionary<string, string> RawReaders = new()
    {
        ["src_mcp/runners/Files/OwnedPaths.cs"] = "reports a link's stored text, one level, for ownership checks of the product's own links",
        ["src_mcp/runners/Files/Junctions.cs"] = "reads back the link the product just made, one level, to verify it",
        ["src_mcp/runners/Snapshots/SnapshotPlanner.cs"] = "asks whether the entry ITSELF is a link, to name it and never follow it",
        ["src_mcp/runners/Files/HandleRead.cs"] = "reads the kernel's own name for an open handle (/proc/self/fd/N), which is already real",
        ["src_mcp/runners/Files/PrivateFolder.cs"] = "refuses a folder that is itself a link",
        ["src_mcp/runners/Files/BoundedFile.cs"] = "refuses a file that is itself a link, on a path the product owns",
        ["src_mcp/runners/Context/ContextAssembler.cs"] = "names an untracked link git listed, and never follows it; git lists no path through a link",
    };

    [Fact]
    public void NoProductionCodeResolvesALinkTargetByItself()
    {
        var sites = ProductionSources.FilesMatching(new Regex(@"\bResolveLinkTarget\b"));

        sites.Keys.Should().BeEmpty(
            "ResolveLinkTarget(...).FullName never resolves the directories inside a link's target and folds `..` lexically; "
            + "a containment decision goes through RealPath. Found: {0}", string.Join(", ", sites.Select(site => $"{site.Key} x{site.Value}")));
    }

    [Fact]
    public void EveryReaderOfRawLinkText_IsEitherTheResolverOrListedWithItsReason()
    {
        var readers = ProductionSources.FilesMatching(new Regex(@"\.LinkTarget\b")).Keys.Except([TheResolver]).Order(StringComparer.Ordinal).ToList();

        readers.Should().Equal(RawReaders.Keys.Order(StringComparer.Ordinal),
            "a new reader of .LinkTarget is a new place that may decide containment from a link; it uses RealPath or is listed here with its reason");
    }

    /// <summary>
    /// Production files that spell BOTH a <c>GetFullPath(</c> and a <c>StartsWith(</c> — the shape of a LEXICAL containment check — and why
    /// each is safe. A new one fails this test: it routes through <c>RealPath</c>, or it is listed here with its reason.
    /// </summary>
    private static readonly Dictionary<string, string> LexicalByDesign = new()
    {
        ["src_mcp/runners/Acp/AcpReadBoundary.cs"] = "a reader's request is placed lexically, then every component is lstat-ed (LinkOnTheWay refuses ANY link) and the bytes are read by handle whose own name is verified (HandleRead)",
        ["src_mcp/runners/Consultation/FilesystemInvariant.cs"] = "compares two paths git itself printed (absolute, already real) to fingerprint a repository; decides no access",
        ["src_mcp/runners/Files/AtomicFile.cs"] = "names a temp file beside the target; no containment decision, and StartsWith is on a file NAME",
        ["src_mcp/runners/Files/HandleRead.cs"] = "strips the long-path prefix of a kernel path and falls back to a spelling the handle check then compares; StartsWith is on a prefix, not containment",
        ["src_mcp/runners/Files/Junctions.cs"] = "classifies a link target as local or network by its spelling (and reads back the link the product made); it deletes and reads nothing by it",
        ["src_mcp/runners/Worktrees/SubmoduleMounts.cs"] = "decides which declared mounts of a tree the product made are INSPECTED (read-only git status); nothing is deleted by it, and the tree's removal is guarded by RealPath",
        ["src_mcp/runners/Worktrees/SubmodulePopulator.cs"] = "the clone source IS the folded path it checked: NoLinksBetween lstat-s every directory from the checkout down to it, so no link can sit on the way",
        ["src_mcp/src/Acp/KimiClearMarkerMode.cs"] = "deletes one file whose DIRECT parent string equals the product's own lock root and whose name matches 16 hex digits plus .survivors.json; a link there is unlinked, never followed",
        ["src_mcp/src/Server/QuestionConsult/QuestionConsultSettings.cs"] = "a deny-list of places: every root is compared as written AND at its RealPath, and a root that cannot be resolved is refused",
    };

    [Fact]
    public void EveryLexicalContainmentShape_IsEitherRoutedThroughRealPathOrListedWithItsReason()
    {
        var shaped = ProductionSources.FilesMentioning("GetFullPath(").Keys
            .Intersect(ProductionSources.FilesMentioning("StartsWith(").Keys)
            .Order(StringComparer.Ordinal)
            .ToList();

        shaped.Should().Equal(LexicalByDesign.Keys.Order(StringComparer.Ordinal),
            "a file that folds a path with GetFullPath and then tests StartsWith is a lexical containment check, which cannot see a link; "
            + "route it through RealPath, or list it here with the reason it is safe. A stale entry fails too.");
    }

    [Fact]
    public void TheScansStillSeeTheirKnownInstances_SoAReformatCannotTurnThemIntoAPass()
    {
        ProductionSources.FilesMatching(new Regex(@"\.LinkTarget\b")).Keys.Should().Contain(RawReaders.Keys,
            "each listed reader really reads .LinkTarget; a stale entry would hide a missing scan");
        ProductionSources.Files().Should().HaveCountGreaterThan(200);
    }
}
