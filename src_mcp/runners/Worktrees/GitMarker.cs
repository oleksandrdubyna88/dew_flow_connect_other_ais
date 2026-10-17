namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// The one answer to "does this directory hold a checkout?" — a <c>.git</c> entry, file or folder.
/// </summary>
/// <remarks>
/// <para>A populated submodule and a linked worktree carry a <c>.git</c> FILE, an ordinary clone a <c>.git</c> folder, an empty
/// submodule mount nothing. Asking for the folder alone answers wrong for the first two, which is why the test is
/// "either".</para>
/// <para>Three callers ask it, so they cannot disagree: <c>SubmodulePopulator</c> (is this a source worth cloning from),
/// <c>SnapshotListing</c> (is this gitlink populated) and <c>CheckoutRoots</c> in <c>CoaiMcp.Store</c> (which checkout did a
/// write land in). It was <c>SubmodulePopulator.HasGit</c>, <c>internal</c> on an <c>internal</c> class, which
/// <c>CoaiMcp</c> cannot see; the body moved here unchanged (E3.S3, reuse move 2: extract the shared half, never copy it).</para>
/// <para><b>A fourth, older copy remains:</b> <c>ConsultantPlaces.HoldsGit</c> (<c>src_mcp/src/Server/Consultation</c>) is the same test written
/// out again. It was not switched in E3 — whether it should be is an open question for the owner — so "three callers" above means
/// the three that use this class.</para>
/// </remarks>
public static class GitMarker
{
    public static bool HasGit(string path) =>
        File.Exists(Path.Combine(path, ".git")) || Directory.Exists(Path.Combine(path, ".git"));
}
