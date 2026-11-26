using System.Globalization;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// Provides the content of a coder job's submodules at the BASE commit to its checks, without a network and without the coder being able to change it (epoch 3 E2 s6, the recorded change). A coder worktree has no submodule content — git leaves an empty folder where
/// a gitlink is — and this repository's own rules (<c>.agents/conventions</c>) are one: 83 tests of the suite are red inside a coder worktree for that and nothing else (<c>RESULTS_coder_proof.md</c>).
/// </summary>
/// <remarks>
/// <para><b>The road.</b> Read <c>.gitmodules</c> and each gitlink AT THE BASE COMMIT (<c>git config --blob</c>, <c>git ls-tree</c> in the main checkout — never its working copy, which can be on another commit, dirty or empty); find the submodule's own git folder in the
/// main checkout (<c>git rev-parse --git-path modules/&lt;name&gt;</c>); and write the pinned commit's tree into a folder of coai's own, <c>&lt;mounts folder&gt;/m&lt;n&gt;</c> (<c>read-tree</c> into an index file of its own, then <c>checkout-index</c> into that folder). The
/// folder is a SIBLING of the job folder (<see cref="Toolchain.BoundaryJob.MountsFolder"/>): a unit binds the job folder writable, and a source reachable through it could be changed from inside, read-only bind or not. A unit then binds it READ-ONLY onto the worktree's empty gitlink
/// folder (<see cref="CoderMounts.Binds"/>); nothing is ever put in the worktree, so git sees the tree it saw.</para>
/// <para><b>Why not bind the main checkout's own folder.</b> That folder is the person's working copy: whatever commit it is on, whatever is changed in it, and its <c>.git</c> file points into the main checkout's <c>.git/modules</c>, which a unit could then reach and read. The pinned commit's files are exactly what the base says.</para>
/// <para><b>Hardened.</b> Every git command is the coder-tree door's (<c>-c core.hooksPath=/dev/null -c core.fsmonitor=false</c> first, the names that move git out of the environment), with the person's global and system config switched off (a filter or an attribute named there must not run
/// on a file of the pinned tree), no sparse checkout, and none of <c>fetch</c>, <c>clone</c>, <c>submodule</c>: a URL in <c>.gitmodules</c> is never read, so a remote that is gone or hostile changes nothing. <c>.gitmodules</c> is the base's own text, its paths are input and are validated
/// (<see cref="IsMountPath"/>: relative, no <c>..</c>, no <c>.git</c>, nothing a bind cannot spell), and so is every path of the tree about to be written. A submodule that cannot be provided is DECLARED with the reason and protected like the others; nothing here throws for it.</para>
/// </remarks>
public sealed class CoderSubmodules(IProcessLauncher launcher)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    /// <summary>The most files of one submodule that are provided: a tree beyond it is not a rules folder.</summary>
    private const int MostFiles = 50_000;

    /// <summary>Sparse checkout off, whatever the submodule's own config says: the whole commit is provided.</summary>
    private static readonly string[] WholeCommit = ["-c", "core.sparseCheckout=false", "-c", "core.sparseCheckoutCone=false"];

    /// <summary>The person's global and system config does not apply: a <c>filter</c> or attribute named there must not run on the pinned files.</summary>
    private static readonly Dictionary<string, string?> NoUserConfig = new(CoderGit.Environment, StringComparer.Ordinal)
    {
        ["GIT_CONFIG_GLOBAL"] = "/dev/null",
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_TERMINAL_PROMPT"] = "0",
    };

    /// <summary>
    /// The submodules the base commit declares and what coai made of each. Writes only under <paramref name="folder"/> (and only when a mount is to be provided); the main checkout and the worktree are read.
    /// </summary>
    /// <param name="mainCheckout">The main checkout: its object store and its <c>.git/modules</c> are read, never written.</param>
    /// <param name="baseSha">The base commit (forty hex digits).</param>
    /// <param name="worktree">The coder's worktree: each mount's target must be a plain empty folder of it.</param>
    /// <param name="folder">The mounts folder (<see cref="Toolchain.BoundaryJob.MountsFolder"/>): each provided mount is a folder <c>m&lt;n&gt;</c> in it.</param>
    public async Task<CoderMounts> ProvideAsync(string mainCheckout, string baseSha, string worktree, string folder, CancellationToken ct = default)
    {
        if (!GitHistory.IsCommitish(baseSha))
        {
            return CoderMounts.None;
        }

        var declared = await DeclaredAsync(mainCheckout, baseSha, ct);
        var made = new List<CoderMount>();
        var provided = 0;
        foreach (var (name, path) in declared)
        {
            var mount = await OneAsync(mainCheckout, baseSha, worktree, folder, name, path, made, provided, ct);
            provided += mount.Provided ? 1 : 0;
            made.Add(mount);
        }

        return new CoderMounts(made);
    }

    /// <summary>Whether <paramref name="path"/> is a relative path a mount may have: <c>/</c>-separated plain names, none empty, <c>.</c>, <c>..</c> or <c>.git</c>, and nothing a bind cannot spell (a space, a colon, a backslash, a control character).</summary>
    internal static bool IsMountPath(string path) =>
        path.Length > 0
        && !path.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ':' or '\\' or '*' or '?' or '[')
        && path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or "..") && !string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) && !string.Equals(segment, CoderMounts.GitmodulesFile, StringComparison.OrdinalIgnoreCase));

    /// <summary>A submodule NAME that can be a folder name below <c>.git/modules</c>: plain names joined by <c>/</c>, none empty, <c>.</c> or <c>..</c>.</summary>
    private static bool IsModuleName(string name) =>
        name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '/') && name.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));

    private sealed record Said(bool Ran, bool Ok, string Out, bool Truncated);

    private async Task<Said> GitAsync(string workingFolder, IReadOnlyList<string> args, CancellationToken ct, IReadOnlyDictionary<string, string?>? extra = null)
    {
        var environment = new Dictionary<string, string?>(NoUserConfig, StringComparer.Ordinal);
        foreach (var (key, value) in extra ?? new Dictionary<string, string?>())
        {
            environment[key] = value;
        }

        var result = await launcher.RunAsync(new ProcessRequest("git", CoderGit.Argv([.. WholeCommit, .. args]), workingFolder) { Timeout = Budget, Environment = environment }, ct);

        return result.TimedOut || result.Cancelled ? new Said(false, false, string.Empty, false) : new Said(true, result.ExitCode == 0, result.StdOut, result.Truncated);
    }

    /// <summary>The <c>(name, path)</c> pairs of the base commit's <c>.gitmodules</c>, in file order; empty when there is none or git could not read it.</summary>
    private async Task<IReadOnlyList<(string Name, string Path)>> DeclaredAsync(string mainCheckout, string baseSha, CancellationToken ct)
    {
        var there = await GitAsync(mainCheckout, ["cat-file", "-e", $"{baseSha}:{CoderMounts.GitmodulesFile}"], ct);
        if (!there.Ran || !there.Ok)
        {
            return [];
        }

        var read = await GitAsync(mainCheckout, ["config", "-z", "--blob", $"{baseSha}:{CoderMounts.GitmodulesFile}", "--get-regexp", @"^submodule\..*\.path$"], ct);
        if (!read.Ran || !read.Ok || read.Truncated)
        {
            return [];
        }

        const string head = "submodule.";
        const string tail = ".path";
        var pairs = new List<(string, string)>();
        foreach (var entry in read.Out.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var cut = entry.IndexOf('\n', StringComparison.Ordinal);
            if (cut > head.Length + tail.Length - 1 && entry.StartsWith(head, StringComparison.Ordinal) && entry[..cut].EndsWith(tail, StringComparison.Ordinal))
            {
                pairs.Add((entry[head.Length..(cut - tail.Length)], entry[(cut + 1)..]));
            }
        }

        return pairs;
    }

    private static CoderMount Not(string name, string path, string pin, string why) => new(name, path, pin, string.Empty, $"{why}, so it was not provided");

    private async Task<CoderMount> OneAsync(string main, string baseSha, string worktree, string folder, string name, string path, IReadOnlyList<CoderMount> before, int index, CancellationToken ct)
    {
        if (!IsMountPath(path))
        {
            return Not(name, path, string.Empty, $"the path '{path}' is not one a mount may have (it must be relative, hold no '..', '.git', space, colon or backslash)");
        }

        if (!IsModuleName(name))
        {
            return Not(name, path, string.Empty, $"the name '{name}' is not one git keeps a submodule folder under");
        }

        if (before.FirstOrDefault(other => other.Provided && (Inside(other.Path, path) || Inside(path, other.Path))) is { } clash)
        {
            return Not(name, path, string.Empty, $"its path lies in, or holds, the mount at '{clash.Path}'");
        }

        var entry = await GitAsync(main, ["--literal-pathspecs", "ls-tree", "-z", "--full-tree", baseSha, "--", path], ct);
        var pin = entry is { Ran: true, Ok: true } ? PinOf(entry.Out, path) : string.Empty;
        if (pin.Length == 0)
        {
            return Not(name, path, string.Empty, "the base commit has no submodule (gitlink) at that path");
        }

        if (!IsPlainFolder(worktree, path))
        {
            return Not(name, path, pin, "the worktree has no plain empty folder at that path to mount onto");
        }

        var module = await ModuleFolderAsync(main, name, ct);
        if (module.Length == 0)
        {
            return Not(name, path, pin, "its git folder is not initialised in the main checkout (run git submodule update --init there)");
        }

        return await MaterialiseAsync(main, module, folder, name, path, pin, index, ct);
    }

    private static bool Inside(string parent, string child) => child.Length > parent.Length && child.StartsWith(parent + "/", StringComparison.Ordinal);

    /// <summary>The commit a <c>ls-tree -z</c> line pins at <paramref name="path"/> when it is a gitlink, else empty.</summary>
    private static string PinOf(string lsTreeZ, string path)
    {
        foreach (var line in lsTreeZ.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            var fields = tab > 0 ? line[..tab].Split(' ') : [];
            if (fields.Length == 3 && fields[0] == "160000" && fields[1] == "commit" && GitHistory.IsCommitish(fields[2]) && string.Equals(line[(tab + 1)..], path, StringComparison.Ordinal))
            {
                return fields[2];
            }
        }

        return string.Empty;
    }

    /// <summary>Whether every folder from the worktree down to the path is a real folder (not a link, not a file) and the last one is empty — what git leaves for a gitlink.</summary>
    private static bool IsPlainFolder(string worktree, string path)
    {
        var walked = worktree;
        foreach (var segment in path.Split('/'))
        {
            walked = Path.Combine(walked, segment);
            if (RealPath.Lstat(walked).Kind != EntryKind.Directory)
            {
                return false;
            }
        }

        return !Directory.EnumerateFileSystemEntries(walked).Any();
    }

    /// <summary>The submodule's own git folder in the main checkout, or empty when it was never initialised there. Git's own answer to "where do modules live", for the main checkout as it is (a linked worktree keeps them elsewhere than a plain one).</summary>
    private async Task<string> ModuleFolderAsync(string main, string name, CancellationToken ct)
    {
        var said = await GitAsync(main, ["rev-parse", "--path-format=absolute", "--git-path", $"modules/{name}"], ct);
        var line = said.Out.Trim();

        return said is { Ran: true, Ok: true } && Path.IsPathFullyQualified(line) && Directory.Exists(line) ? line : string.Empty;
    }

    private async Task<CoderMount> MaterialiseAsync(string main, string module, string folder, string name, string path, string pin, int index, CancellationToken ct)
    {
        var has = await GitAsync(main, [$"--git-dir={module}", "cat-file", "-e", $"{pin}^{{commit}}"], ct);
        if (!has.Ran || !has.Ok)
        {
            return Not(name, path, pin, $"the pinned commit {pin[..12]} is not in the submodule's own object store in the main checkout");
        }

        var listed = await GitAsync(main, [$"--git-dir={module}", "ls-tree", "-r", "-z", "--full-tree", pin], ct);
        if (!listed.Ran || !listed.Ok || listed.Truncated)
        {
            return Not(name, path, pin, "its tree could not be listed");
        }

        var files = FilesOf(listed.Out);
        if (files is null || files.Count == 0 || files.Count > MostFiles)
        {
            return Not(name, path, pin, files is null ? "its tree holds a path or a link that could leave the folder it is written into" : "its tree is empty or too large");
        }

        var source = Path.Combine(folder, $"m{index.ToString(CultureInfo.InvariantCulture)}");
        var indexFile = Path.Combine(folder, $".index-m{index.ToString(CultureInfo.InvariantCulture)}");
        try
        {
            Directory.CreateDirectory(folder);
            DeleteQuietly(source);
            Directory.CreateDirectory(source);
            var env = new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = indexFile };
            var read = await GitAsync(source, [$"--git-dir={module}", $"--work-tree={source}", "read-tree", pin], ct, env);
            var wrote = read is { Ran: true, Ok: true } ? await GitAsync(source, [$"--git-dir={module}", $"--work-tree={source}", "checkout-index", "-a"], ct, env) : read;
            if (!wrote.Ran || !wrote.Ok || files.Any(file => RealPath.Lstat(Path.Combine(source, file)).Kind == EntryKind.Missing))
            {
                DeleteQuietly(source);

                return Not(name, path, pin, "git could not write the pinned tree out");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(source);

            return Not(name, path, pin, $"its folder could not be made ({e.Message})");
        }
        finally
        {
            DeleteQuietly(indexFile);
            DeleteQuietly(indexFile + ".lock");
        }

        return new CoderMount(name, path, pin, source, string.Empty);
    }

    /// <summary>
    /// The file paths of a <c>ls-tree -r -z</c> listing — or null when anything about them could write outside the folder: a path that is absolute or holds <c>.</c>/<c>..</c>/<c>.git</c> segments, or a symbolic link (mode 120000), which is never written.
    /// Gitlinks (a nested submodule) are folders git makes empty and are not files.
    /// </summary>
    private static List<string>? FilesOf(string lsTreeZ)
    {
        var files = new List<string>();
        foreach (var line in lsTreeZ.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            var mode = tab > 0 ? line[..line.IndexOf(' ', StringComparison.Ordinal)] : string.Empty;
            var file = tab > 0 ? line[(tab + 1)..] : string.Empty;
            if (file.Length == 0 || file.StartsWith('/') || file.Contains('\\', StringComparison.Ordinal) || file.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".." || string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase)) || mode is "120000" or "")
            {
                return null;
            }

            if (mode != "160000")
            {
                files.Add(file);
            }
        }

        return files;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                GitScratch.DeleteEvenIfReadOnly(path);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: what is left is in coai's own folder and is removed with it.
        }
    }
}
