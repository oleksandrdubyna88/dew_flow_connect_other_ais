namespace CoaiMcp.Core.Feature;

/// <summary>
/// How much of each kind of material a feature reviewer is sent — constants, each traced to a
/// measurement (plan §6, S0.2), never typed from the plan's first guess.
/// </summary>
/// <remarks>
/// <para><b>Measured 2026-09-25</b> by running the product's own AOT <c>coai-mcp --outline</c> over
/// every file three shipped features of this repository changed, read at their head commit:</para>
/// <list type="table">
/// <item><term>consultant</term><description><c>92c22bd8..0e8fc6a7</c>, 126 files at head, 105 outlined: <b>166.1 KB</b> of outline; plan 52.7 KB; not-outlined list 1.5 KB.</description></item>
/// <item><term><c>review_document</c>, PR #230</term><description><c>ab16744c..3979e765</c>, 78 files, 64 outlined: <b>96.6 KB</b>; plan 36.3 KB; list 1.0 KB.</description></item>
/// <item><term>the S8 notices</term><description><c>7383170c..f82566fa</c>, 258 files, 202 outlined: <b>322.4 KB</b>; plan 49.2 KB; list 4.2 KB. Its range carries 61 commits, other work merged between its epics included — which is what <c>base..head</c> really is.</description></item>
/// </list>
/// <para>Per outlined file: p50 0.9–1.1 KB, p90 3.1–3.7 KB, largest 11.4–12.7 KB (<c>PanelService.cs</c>
/// every time). An outline is 4.6–5.3 % of the source it describes.</para>
/// <para><b>Only what S0.2 could measure is here.</b> The epics, the lessons and the gate's history
/// do not exist until Epic 2 builds them; their budgets are added with the story that measures
/// them, not guessed now.</para>
/// </remarks>
public static class FeatureBudget
{
    /// <summary>The plan, at head, before it is cut: 64 KB.</summary>
    /// <remarks>
    /// The largest plan measured was 52.7 KB (the consultant's); 64 KB holds it with a fifth to spare,
    /// so the plan's figure stands — now as a measured one.
    /// </remarks>
    public const int PlanBytes = 64 * 1024;

    /// <summary>All outlines together: 168 KB — raised from the plan's 112 KB by the measurement.</summary>
    /// <remarks>
    /// 112 KB held only the smallest of the three features whole (PR #230, 96.6 KB): it would have
    /// cut the consultant by a third and the notices by two thirds before a reviewer read a line.
    /// 168 KB is the median feature (the consultant, 166.1 KB) rounded up to 8 KB, so a typical
    /// feature arrives whole and only an unusually wide range — the notices, 322.4 KB — goes through
    /// the deterministic cutting (collapse large files' unchanged members, then drop whole files by
    /// change size, every dropped file named).
    /// </remarks>
    public const int OutlineBytes = 168 * 1024;

    /// <summary>A file whose outline exceeds this is the first to have its unchanged members collapsed: 4 KB.</summary>
    /// <remarks>
    /// The p90 of per-file outline size was 3.1–3.7 KB across the three features; above 4 KB is the
    /// top tenth — <c>PanelService.cs</c>, the panel's two big files — where collapsing buys the most.
    /// </remarks>
    public const int CollapseAboveBytes = 4 * 1024;

    /// <summary>Reserved for "Files not outlined" and "What this context left out": 8 KB, never cut.</summary>
    /// <remarks>
    /// The plan reserved 4 KB; the notices' not-outlined list alone measured 4.2 KB (56 files), so 4 KB
    /// would have had to truncate the one section the plan says is never truncated.
    /// </remarks>
    public const int OmissionsReserveBytes = 8 * 1024;

    /// <summary>The most changed files a feature review outlines: 400.</summary>
    /// <remarks>
    /// The widest range measured had 258 files present at head; 400 leaves half as much again. Past it
    /// the files are listed, not outlined.
    /// </remarks>
    public const int MaxOutlinedFiles = 400;
}
