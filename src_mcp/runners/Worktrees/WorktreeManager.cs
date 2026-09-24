using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>A git operation the round cannot proceed without failed; the sentence says which.</summary>
public sealed class WorktreeException(string operation, string detail)
    : Exception($"git {operation}: {detail}");

/// <summary>
/// One round's shared, read-only tree. Disposal is the <c>finally</c> the epic demands: however
/// the fan-out ends, the worktree goes.
/// </summary>
/// <remarks>
/// Disposal NEVER throws (S2 of todo/PLAN_a_failed_round_can_be_retried.md). It used to, and it ran
/// inside the round's <c>await using</c>: a tree Windows would not let go of replaced a finished
/// round's verdict and findings with <c>{"error":"git worktree remove: …"}</c> — after the session
/// had already saved them as pending — and replaced a body exception with its own, so the real cause
/// was gone too. A tree that cannot be removed now is a warning and a job for the next sweep.
/// </remarks>
public sealed class WorktreeLease(WorktreeManager manager, string repoPath, string path, string sha)
    : IAsyncDisposable
{
    public string Path { get; } = path;

    public string Sha { get; } = sha;

    public async ValueTask DisposeAsync() => await manager.ReleaseAsync(repoPath, Path);
}

/// <summary>Who made a round tree: the server process, named so another server can tell if it is gone.</summary>
internal sealed record TreeOwner(int Pid, DateTime StartedUtc);

[System.Text.Json.Serialization.JsonSerializable(typeof(TreeOwner))]
internal sealed partial class TreeOwnerContext : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>
/// One worktree per ROUND ATTEMPT, pinned to a SHA, outside the repository, shared by every reviewer
/// in the round — six read-only reviewers share a tree safely, and six checkouts of a moving branch
/// would be six different inputs to what is meant to be one comparison.
/// </summary>
/// <remarks>
/// <para><b>A failed round's tree can never block the next attempt</b> (S2 of
/// todo/PLAN_a_failed_round_can_be_retried.md). Two mechanisms used to, both reproduced against git
/// 2.55: a <c>worktree add</c> killed half-way leaves <c>.git/worktrees/&lt;name&gt;/locked</c> =
/// <c>initializing</c>, which <c>remove --force</c> refuses and <c>prune</c> skips, so every later add
/// at that path failed "missing but locked" for ever; and a file held open on Windows left the
/// directory behind, so a retry at the same deterministic path failed "already exists". Now:</para>
/// <list type="bullet">
/// <item>a path per ATTEMPT (<c>coai-wt-{session}-r{round}-{attempt}</c>), so no leftover shares a retry's path;</item>
/// <item>an OWNER marker beside every tree — the server's pid and its start time. The root is
/// machine-local (<see cref="MachineLocalRoot"/>), so every process that touches it is on this machine
/// and a pid means something; the start time stops a reused number passing for the owner. A paused
/// owner is still alive and is never reaped — there is no heartbeat to go stale;</item>
/// <item>a sweep before EVERY round of this session's own leftovers, and on <c>open</c> of every tree
/// whose owner is gone — never a live tree of another server, which the old sweep removed by prefix;</item>
/// <item>removal that unlocks, forces twice, retries briefly and otherwise renames the tree to
/// <c>coai-wt-trash-*</c> for the next sweep, and never throws.</item>
/// </list>
/// <para>Paths carry the <c>coai-wt-</c> prefix and live under OUR storage root, so nothing here ever
/// touches a tree it did not create — a person's checkout, or a review tree (<c>coai-review-</c>).</para>
/// </remarks>
public sealed class WorktreeManager(IProcessLauncher launcher, string storageRoot, Action<string>? note = null)
{
    private const string Prefix = "coai-wt-";
    private const string TrashPrefix = "coai-wt-trash-";
    private const string OwnerSuffix = ".owner";

    /// <summary>How long trash may sit before it is said out loud — once, per process.</summary>
    private static readonly TimeSpan TrashNamedAfter = TimeSpan.FromHours(24);

    private static readonly HashSet<string> TrashAlreadyNamed = new(StringComparer.OrdinalIgnoreCase);

    private readonly SubmodulePopulator _submodules = new(launcher);

    /// <summary>
    /// Where round trees live on a real server: machine-local, beside the review trees, and never
    /// under the data dir — which is routinely a network share, where a linked worktree (its
    /// <c>.git</c> file holds an absolute path into one machine's repository) is broken for every
    /// other machine, and where a pid would mean nothing.
    /// </summary>
    public static string MachineLocalRoot => ReviewTreeRoot.MachineLocal("round-worktrees");

    public async Task<string> ResolveShaAsync(string repoPath, string branch)
    {
        var result = await Git(repoPath, ReviewTreeRoot.Asking, "rev-parse", "--verify", $"{branch}^{{commit}}");
        return result.ExitCode == 0
            ? result.StdOut.Trim()
            : throw new WorktreeException("rev-parse", $"cannot resolve '{branch}': {result.StdErr.Trim()}");
    }

    public async Task<WorktreeLease> AddAsync(string repoPath, string sha, string sessionId, int round)
    {
        Directory.CreateDirectory(storageRoot);
        // THIS session's leftovers first — a round of the same session that died, or whose tree could
        // not be removed. One mutating call per session at a time, so none of them is in use.
        await SweepAsync(repoPath, name => name.StartsWith($"{Prefix}{sessionId}-", StringComparison.Ordinal));

        var path = Path.Combine(storageRoot, $"{Prefix}{sessionId}-r{round}-{ShortId()}");
        // The owner BEFORE the tree: an add killed half-way leaves a marker naming its maker, so the
        // sweep can tell a half-made tree of a dead server from one being made right now.
        WriteOwner(path);
        var result = await Git(repoPath, ReviewTreeRoot.Checking, "worktree", "add", "--detach", path, sha);
        if (result.ExitCode != 0)
        {
            await EraseAsync(repoPath, path);
            throw new WorktreeException("worktree add", FatalLine(result.StdErr));
        }

        // A linked worktree gets no submodules from git, and in this family the project's own
        // written rules are exactly that — see SubmodulePopulator for what the reviewers were
        // being handed instead.
        //
        // The tree exists from here on but the LEASE does not, so nothing would run the finally
        // that removes it: an exception escaping population would leave an orphan behind.
        try
        {
            await _submodules.PopulateAsync(repoPath, path);
        }
        catch
        {
            await EraseAsync(repoPath, path);
            throw;
        }

        return new WorktreeLease(this, repoPath, path, sha);
    }

    /// <summary>A round's tree, given back. Never throws — see <see cref="WorktreeLease"/>.</summary>
    public async Task ReleaseAsync(string repoPath, string path)
    {
        try
        {
            await EraseAsync(repoPath, path);
        }
        catch (Exception e)
        {
            note?.Invoke($"could not give back {path}: {e.Message} — the next sweep takes it");
        }
    }

    /// <summary>
    /// Clears what a DEAD server left behind: our trees whose owner is gone, half-made registrations
    /// included, and whatever trash can now be deleted. Called on <c>open</c>.
    /// </summary>
    /// <remarks>
    /// A tree whose owner is alive is left alone, whoever that owner is. It used to remove every
    /// <c>coai-wt-*</c> under the root, and every Claude window runs its own server on one data
    /// directory — so an <c>open</c> in one window took the running round's tree out from under
    /// another. Never throws: a directory that will not go is a warning, never a failed <c>open</c>.
    /// </remarks>
    public Task PruneOursAsync(string repoPath) => SweepAsync(repoPath, static _ => false);

    /// <summary>
    /// Our trees under the root: <paramref name="alwaysOurs"/> ones regardless of their owner, the
    /// rest only when the owner is gone; then git's own prune, then the trash.
    /// </summary>
    private async Task SweepAsync(string repoPath, Func<string, bool> alwaysOurs)
    {
        try
        {
            foreach (var path in (await ListOursAsync(repoPath)).Concat(OurDirectories()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await EraseIfUnusedAsync(repoPath, path, alwaysOurs);
            }

            await Git(repoPath, ReviewTreeRoot.Asking, "worktree", "prune");
            EmptyTheTrash();
        }
        catch (Exception e)
        {
            note?.Invoke($"the worktree sweep stopped early: {e.Message}");
        }
    }

    private async Task EraseIfUnusedAsync(string repoPath, string path, Func<string, bool> alwaysOurs)
    {
        if (alwaysOurs(Path.GetFileName(path)) || !OwnerIsAlive(path))
        {
            await EraseAsync(repoPath, path);
        }
    }

    /// <summary>
    /// Gone by every means we have, strictly for a <c>coai-wt-</c> path under our root: unlock (a
    /// half-made add is locked "initializing"), remove forced twice, delete with a short retry for a
    /// scanner, and as a last resort a rename out of the way so no retry ever meets it.
    /// </summary>
    private async Task EraseAsync(string repoPath, string path)
    {
        if (!IsOurs(path))
        {
            return;
        }

        await Git(repoPath, ReviewTreeRoot.Asking, "worktree", "unlock", path);
        await Git(repoPath, ReviewTreeRoot.Erasing, "worktree", "remove", "-f", "-f", path);
        if (Directory.Exists(path) && !await DeletedAsync(path))
        {
            MoveAside(path);
        }

        // The registration may outlive the directory — git drops it on a failed remove, but a
        // half-made add keeps it — and prune clears a registration whose directory is gone.
        await Git(repoPath, ReviewTreeRoot.Asking, "worktree", "prune");
        TryDelete(OwnerFile(path));
    }

    /// <summary>A tree that will not go is renamed out of every retry's way, for the next sweep.</summary>
    private void MoveAside(string path)
    {
        var trash = Path.Combine(storageRoot, $"{TrashPrefix}{ShortId()}");
        try
        {
            Directory.Move(path, trash);
            note?.Invoke($"{path} could not be deleted (a file in it is held open); moved aside to {trash}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note?.Invoke($"{path} could not be deleted or moved aside ({e.Message}); the next sweep tries again");
        }
    }

    /// <summary>
    /// Deleted within three short waits, or not. A scanner or an indexer usually lets go within a
    /// moment; a process that holds a file for the length of a round does not, and the waits cost
    /// under a second.
    /// </summary>
    private static async Task<bool> DeletedAsync(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (DeletedNow(path))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)));
        }

        return false;
    }

    private static bool DeletedNow(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void EmptyTheTrash()
    {
        if (!Directory.Exists(storageRoot))
        {
            return;
        }

        ForgetDeadMarkers();
        DeleteTrash();
    }

    /// <summary>
    /// A marker whose tree is gone and whose owner is gone names nothing any more — left, markers
    /// would be the one thing under the root that only ever grows.
    /// </summary>
    private void ForgetDeadMarkers()
    {
        foreach (var marker in Directory.GetFiles(storageRoot, $"{Prefix}*{OwnerSuffix}"))
        {
            var tree = marker[..^OwnerSuffix.Length];
            if (!Directory.Exists(tree) && !OwnerIsAlive(tree))
            {
                TryDelete(marker);
            }
        }
    }

    private void DeleteTrash()
    {
        foreach (var trash in Directory.GetDirectories(storageRoot, $"{TrashPrefix}*"))
        {
            if (!DeletedNow(trash))
            {
                NameIfOld(trash);
            }
        }
    }

    /// <summary>
    /// Bounded by saying so: trash that outlives a day is named once per process, never left to grow
    /// in silence (plan round, gemini).
    /// </summary>
    private void NameIfOld(string trash)
    {
        if (DateTime.UtcNow - Directory.GetCreationTimeUtc(trash) > TrashNamedAfter && Remember(trash))
        {
            note?.Invoke($"{trash} has been held open for over a day and cannot be deleted");
        }
    }

    private static bool Remember(string trash)
    {
        lock (TrashAlreadyNamed)
        {
            return TrashAlreadyNamed.Add(trash);
        }
    }

    internal async Task<IReadOnlyList<string>> ListOursAsync(string repoPath)
    {
        var result = await Git(repoPath, ReviewTreeRoot.Asking, "worktree", "list", "--porcelain");
        return [.. result.StdOut
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith("worktree ", StringComparison.Ordinal))
            .Select(l => l["worktree ".Length..])
            .Where(IsOurs)];
    }

    /// <summary>Directories under the root with our prefix — a tree git no longer lists is still ours to remove.</summary>
    private IEnumerable<string> OurDirectories() =>
        Directory.Exists(storageRoot)
            ? Directory.GetDirectories(storageRoot, $"{Prefix}*")
                .Where(d => !Path.GetFileName(d).StartsWith(TrashPrefix, StringComparison.Ordinal))
            : [];

    /// <summary>Our prefix AND our root — the two independent guards, both required.</summary>
    private bool IsOurs(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('/', '\\'));
        var root = Path.GetFullPath(storageRoot).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        return name.StartsWith(Prefix, StringComparison.Ordinal)
            && !name.StartsWith(TrashPrefix, StringComparison.Ordinal)
            && Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string OwnerFile(string path) => path.TrimEnd('/', '\\') + OwnerSuffix;

    /// <summary>Eight hex characters: enough that two attempts never share a path, short enough to read.</summary>
    private static string ShortId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// The marker, written WHOLE or not at all: to a temporary name, then renamed over.
    /// </summary>
    /// <remarks>
    /// Another server's sweep reads an unreadable marker as a dead owner, so a half-written one — an
    /// empty file for the moment between create and write — would let it take a tree being made
    /// right now (code round, gemini). A rename within one directory is atomic, so a reader sees no
    /// marker or the whole marker, never a part; the temporary name does not match the marker glob.
    /// </remarks>
    private static void WriteOwner(string path)
    {
        using var self = Process.GetCurrentProcess();
        var marker = OwnerFile(path);
        var partial = $"{marker}.{ShortId()}.tmp";
        File.WriteAllText(
            partial,
            JsonSerializer.Serialize(new TreeOwner(self.Id, self.StartTime.ToUniversalTime()), TreeOwnerContext.Default.TreeOwner));
        File.Move(partial, marker, overwrite: true);
    }

    /// <summary>
    /// Whether the server that made this tree is still running. No marker is no owner: a tree made by
    /// an older build, or a marker that could not be written, is one nobody is using now.
    /// </summary>
    private static bool OwnerIsAlive(string path)
    {
        try
        {
            var owner = JsonSerializer.Deserialize(File.ReadAllText(OwnerFile(path)), TreeOwnerContext.Default.TreeOwner);
            return owner is not null && IsRunning(owner);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>The pid is alive AND is the process that wrote the marker — a reused number is not.</summary>
    private static bool IsRunning(TreeOwner owner) =>
        ProcessTracking.StartedAt(owner.Pid) is { } started && OrphanSweep.Same(started, owner.StartedUtc);

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note?.Invoke($"could not delete {file}: {e.Message}");
        }
    }

    /// <summary>git's own verdict, not its preamble: the first <c>fatal:</c> line when there is one.</summary>
    internal static string FatalLine(string stderr) =>
        stderr.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("fatal:", StringComparison.Ordinal))
        ?? stderr.Trim();

    private Task<ProcessResult> Git(string repoPath, TimeSpan budget, params string[] args) =>
        launcher.RunAsync(new ProcessRequest("git", args, repoPath)
        {
            Timeout = budget,
        });
}
