namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// The one answer to "does this directory hold a checkout?" — a <c>.git</c> entry, file or folder.
/// </summary>
/// <remarks>
/// <para>A populated submodule and a linked worktree carry a <c>.git</c> FILE, an ordinary clone a <c>.git</c> folder, an empty
/// submodule mount nothing. Asking for the folder alone answers wrong for the first two, which is why the test is
/// "either".</para>
/// <para>Four callers ask it, so they cannot disagree: <c>SubmodulePopulator</c> (is this a source worth cloning from),
/// <c>SnapshotListing</c> (is this gitlink populated), <c>CheckoutRoots</c> in <c>CoaiMcp.Store</c> (which checkout did a
/// write land in) and <c>ConsultantPlaces</c> in <c>CoaiMcp.Server.Consultation</c> (the top of the work tree above a granted
/// folder, where its link walk stops). It was <c>SubmodulePopulator.HasGit</c>, <c>internal</c> on an <c>internal</c> class, which
/// <c>CoaiMcp</c> cannot see; the body moved here unchanged (E3.S3, reuse move 2: extract the shared half, never copy it), and
/// <c>ConsultantPlaces</c>' own copy of the same test was switched to it on 2026-10-18 (owner-approved).</para>
/// </remarks>
public static class GitMarker
{
    public static bool HasGit(string path) =>
        File.Exists(Path.Combine(path, ".git")) || Directory.Exists(Path.Combine(path, ".git"));
}
