using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The unit of the agy CODER, as pure values (epoch 3 E3 s6 t3; judgement U2, U3, U6, U7, U8, U9, AG-B1, AG-B3, AG-R1, AG-R2; SPEC C6): the measured unit of <c>RESULTS_agy_coder.md</c> section 3. Where the claude coder's unit binds the main checkout and the coder root READ-ONLY, this one HIDES them
/// (an empty tmpfs, writable and gone with the unit, so nothing of them can be read or listed and a write beside the worktree does not reach the disk); only <c>&lt;main&gt;/.git</c> and the worktree (with its own <c>.git</c> read-only) are bound back. Its working directory is the guard folder and
/// NEVER the worktree (AG-B1); the guard folder is bound read-only on top of the vendor's own folder (AG-B3), and so is the program's folder (U9). The hook handler's folder is bound read-only too: the handler is coai's own program, started by agy inside the unit.
/// </summary>
/// <remarks>
/// <para><b>One folder of its own.</b> Everything agy needs lives under <see cref="VendorFolder"/> (<c>&lt;job&gt;/coder/agy</c> (<see cref="BoundaryJob.CoderVendorFolder"/>)): its home with the sign-in COPY of the job, the guard folder, and the records of the guard. The folder of another vendor of the job is not bound into this unit.</para>
/// <para>These are requests until the preflight has seen the answer (<see cref="AgyCoderPreflight"/>): the property list is never a boundary on its own.</para>
/// </remarks>
internal static class AgyCoderUnit
{
    /// <summary>The file of agy's sign-in, relative to its home (<c>~/.gemini/antigravity-cli/antigravity-oauth-token</c>): the one file agy needs (results section 0.3).</summary>
    public const string SignInRelative = ".gemini/antigravity-cli/antigravity-oauth-token";

    /// <summary>The folder every file of the agy coder of a job lives in.</summary>
    public static string VendorFolder(BoundaryJob job) => job.CoderVendorFolder(AgyCoderVendor.VendorId);

    /// <summary>The <c>HOME</c> of agy inside the unit: its sign-in copy, its conversations, its logs.</summary>
    public static string HomeFolder(BoundaryJob job) => job.CoderHomeOf(AgyCoderVendor.VendorId);

    /// <summary>The guard folder: agy's working directory, bound read-only in the unit.</summary>
    public static string GuardFolder(BoundaryJob job) => Path.Combine(VendorFolder(job), "guard");

    /// <summary>The records of the guard: a file of the vendor's folder outside the worktree and outside the guard folder.</summary>
    public static string RecordsFile(BoundaryJob job) => Path.Combine(VendorFolder(job), "guard-records.jsonl");

    /// <summary>Where the sign-in copy lies inside the unit's home.</summary>
    public static string SignInFile(BoundaryJob job) => Path.Combine(HomeFolder(job), ".gemini", "antigravity-cli", "antigravity-oauth-token");

    /// <summary>The folders coai makes before the preflight, in order.</summary>
    public static IReadOnlyList<string> Folders(BoundaryJob job) =>
        [VendorFolder(job), HomeFolder(job), Path.Combine(HomeFolder(job), ".gemini"), Path.Combine(HomeFolder(job), ".gemini", "antigravity-cli"), job.CoderTempOf(AgyCoderVendor.VendorId), job.CoderVarTempOf(AgyCoderVendor.VendorId)];

    /// <summary>The <c>-p</c> properties of the agy coder's unit, in the order they are given.</summary>
    public static IReadOnlyList<string> Properties(BoundaryHost host, BoundaryJob job, CoderProgram program, HookHandler handler, TimeSpan runtimeMax)
    {
        var tree = job.Tree;

        return
        [
            .. BoundaryUnit.Isolation,
            // The main checkout and the coder root are HIDDEN (U2, U3): an empty tmpfs, writable because that is what the measure used and a write lands nowhere; only the main .git (read-only) and the worktree are bound back.
            $"TemporaryFileSystem={tree.MainCheckout}",
            $"BindReadOnlyPaths={Path.Combine(tree.MainCheckout, ".git")}",
            $"TemporaryFileSystem={job.CoderRoot}",
            $"BindPaths={tree.Root}",
            $"BindReadOnlyPaths={tree.GitEntry}",
            .. BoundaryUnit.Mounts(job),
            $"TemporaryFileSystem={job.JobCover}:ro",
            $"BindPaths={VendorFolder(job)}",
            $"BindReadOnlyPaths={GuardFolder(job)}",
            $"BindPaths={job.CoderTempOf(AgyCoderVendor.VendorId)}:{BoundaryUnit.UnitTemp}",
            $"BindPaths={job.CoderVarTempOf(AgyCoderVendor.VendorId)}:{BoundaryUnit.UnitVarTemp}",
            .. BoundaryUnit.KeysHidden(job),
            // The real sign-in files of the person — claude's and agy's own, at their real paths — are in no unit but as the job's copy (the coordinator's order of 2026-11-26).
            .. BoundaryUnit.SignInHidden(host),
            .. program.BindFolder.Length > 0 ? [$"BindReadOnlyPaths={program.BindFolder}"] : Array.Empty<string>(),
            .. HandlerFolders(handler, program).Select(folder => $"BindReadOnlyPaths={folder}"),
            .. host.ResolverFile.Length > 0 ? [$"BindReadOnlyPaths={host.ResolverFile}"] : Array.Empty<string>(),
            .. BoundaryUnit.Hardening,
            "PrivateNetwork=no",
            .. BoundaryUnit.Limits(job, runtimeMax),
        ];
    }

    private static readonly string[] SystemFolders = ["/usr", "/bin", "/sbin", "/lib", "/lib64", "/opt"];

    /// <summary>The folders that hold the files of the hook handler (its program and the library it is given), each once, when the unit does not see them anyway — they are bound read-only, so the handler cannot be replaced from inside.</summary>
    public static IReadOnlyList<string> HandlerFolders(HookHandler handler, CoderProgram program) =>
        [.. new[] { handler.Executable }.Concat(handler.Prefix)
            .Where(part => Path.IsPathFullyQualified(part))
            .Select(part => RealPath.Resolve(part) is { Ok: true } real ? Path.GetDirectoryName(real.Path) ?? string.Empty : string.Empty)
            .Where(folder => folder.Length > 0 && folder != Path.GetPathRoot(folder) && folder != program.BindFolder && !SystemFolders.Any(system => RealPath.IsInside(system, folder, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)];

    /// <summary>The environment of the unit as <c>env -i</c> sets it inside: the allow-list and nothing else. No variable for the sign-in, no key, no <c>COAI_</c> name (U7).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Environment(BoundaryJob job) =>
    [
        new("PATH", BoundaryHost.SystemPath),
        new("HOME", HomeFolder(job)),
        new("TMPDIR", BoundaryUnit.UnitTemp),
        new("LANG", "C.UTF-8"),
        new("TERM", "dumb"),
        new("NO_COLOR", "1"),
        new("GIT_TERMINAL_PROMPT", "0"),
    ];
}
