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

    /// <summary>
    /// The part of <see cref="OutlineBytes"/> the member hunks are given FIRST: 56 KB, a third — the total
    /// stays 168 KB.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a reserve at all</b> (the coordinator's decision for S2.2b, 2026-09-26). S2.2a composed
    /// the outline first and gave the hunks whatever it left; on the S8 notices range the outline alone
    /// overfilled the 168 KB and NO hunk fitted, so on the median-and-larger features the hybrid D22
    /// confirmed collapsed back to the outline-only arm — the arm the feature-pack trial found worse
    /// (0.68 high-value findings per cell against 2.00, and 0 of the 4 planted defects whose hunk was
    /// cut).</para>
    /// <para><b>How it is spent.</b> The hunks are placed first, the smallest change first with the
    /// per-member cap unchanged, up to this reserve; the outline gets the remainder and is cut as before
    /// (unchanged members collapse first, then whole files drop by name); room the hunks did not use flows
    /// back to the outline, and room the outline did not use flows on to further hunks.</para>
    /// <para>A third is a decision, not a measurement: it keeps two thirds of the map on the widest range
    /// measured, and a five-line edit renders to well under half a kilobyte with its heading, so the
    /// reserve holds on the order of a hundred of the small edits the trial found defects in.</para>
    /// </remarks>
    public const int HunkReserveBytes = 56 * 1024;

    /// <summary>The hunk reserve of an outline section of <paramref name="outlineBudget"/> bytes — the same third, never more than <see cref="HunkReserveBytes"/>.</summary>
    /// <remarks>Scaled rather than fixed, so a smaller section (a test's, a narrower limit) keeps two thirds of itself for the outline.</remarks>
    public static int HunkReserveFor(int outlineBudget) =>
        (int)Math.Min(HunkReserveBytes, (long)Math.Max(outlineBudget, 0) * HunkReserveBytes / OutlineBytes);

    /// <summary>A file whose outline exceeds this is the first to have its unchanged members collapsed: 4 KB.</summary>
    /// <remarks>
    /// The p90 of per-file outline size was 3.1–3.7 KB across the three features; above 4 KB is the
    /// top tenth — <c>PanelService.cs</c>, the panel's two big files — where collapsing buys the most.
    /// </remarks>
    public const int CollapseAboveBytes = 4 * 1024;

    /// <summary>The most one changed member's hunk may take of the outline budget: 8 KB, the rest named.</summary>
    /// <remarks>
    /// <para><b>From the feature-pack trial's arm F</b> (2026-09-26, <c>research/RESULTS_feature_pack_trial.md</c>):
    /// uncapped, a few enormous members starved the rest — on tsx2 six units filled all 62 KB of hunk room
    /// (the largest alone 26 KB, so about 10 KB each) while 735 were cut, 664 of them with 20 changed lines
    /// or fewer; and the reviewer found 0 of the 4 planted defects whose member was cut against 6 of the 9
    /// whose hunk was shown.</para>
    /// <para>8 KB is below the ~10 KB those six units averaged (the largest 26 KB) and well above a member
    /// of 20 changed lines — an estimate, not a trial number: at 60–80 bytes a diff line with its context,
    /// such a member renders to 1.5–3 KB — so it truncates the shape that starved the budget and leaves the
    /// small edits whole (the plan's D22 row, 2026-09-26, asks for exactly this cap). A truncated hunk ends
    /// with "[N more changed lines — ask for source]".</para>
    /// </remarks>
    public const int MaxHunkBytesPerMember = 8 * 1024;

    /// <summary>Reserved for "Files not outlined" and "What this context left out": 12 KB, never cut.</summary>
    /// <remarks>
    /// <para>The plan reserved 4 KB; the notices' not-outlined list alone measured 4.2 KB (56 files), so 4 KB
    /// would have had to truncate the one section the plan says is never truncated. S1.3 set 8 KB from that.</para>
    /// <para><b>Raised to 12 KB by the feature-pack trial</b> (2026-09-26,
    /// <c>research/RESULTS_feature_pack_trial.md</c>): once D22's hybrid names every member hunk it cut for
    /// the outline budget, the omissions of the trial's cs1 feature exceeded 8 KB. A list longer still is
    /// said more briefly by <c>OmissionsRenderer</c>, never cut.</para>
    /// </remarks>
    public const int OmissionsReserveBytes = 12 * 1024;

    /// <summary>The epics as the reviewer reads them: 16 KB — the plan's figure, equal to the input cap.</summary>
    /// <remarks>
    /// <c>FeatureInputs.EpicsMaxBytes</c> refuses a larger argument, so only the rendering's headings can
    /// push the section past this — and then it is cut and named. Not re-measured by S2.2: the trial
    /// built its packs under this figure (plan §4.6).
    /// </remarks>
    public const int EpicsBytes = 16 * 1024;

    /// <summary>The lessons as the reviewer reads them: 16 KB — the plan's figure (§4.6).</summary>
    /// <remarks>
    /// Half of the 32 KB the argument may weigh (<c>FeatureInputs.LessonsMaxBytes</c>, §4.5): a caller may
    /// write more than a reviewer is shown, and what is not shown is named under "What this context left
    /// out". Not re-measured by S2.2: the trial built its packs under this figure, and D21 kept the
    /// lessons in the pack on the trial's numbers.
    /// </remarks>
    public const int LessonsBytes = 16 * 1024;

    /// <summary>The gate's history of this work: 24 KB — the plan's figure (§4.6, §4.8).</summary>
    /// <remarks>The section is filled by S2.3; the trial built its packs under this figure.</remarks>
    public const int HistoryBytes = 24 * 1024;

    /// <summary>The most changed files a feature review outlines: 400.</summary>
    /// <remarks>
    /// The widest range measured had 258 files present at head; 400 leaves half as much again. Past it
    /// the files are listed, not outlined.
    /// </remarks>
    public const int MaxOutlinedFiles = 400;
}
