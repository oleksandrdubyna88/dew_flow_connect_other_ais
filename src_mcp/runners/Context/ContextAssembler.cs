using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Context;

/// <summary>What never reaches a reviewer, before any budget is spent on it.</summary>
public static class DiffExclusions
{
    /// <summary>On top of whatever the repository's own <c>.gitignore</c> already excludes —
    /// git never diffs untracked files, so ignored build output is out by construction; these
    /// remove the TRACKED noise: lock files, vendored output, minified artefacts.</summary>
    public static readonly IReadOnlyList<string> Default =
    [
        "**/package-lock.json",
        "**/yarn.lock",
        "**/pnpm-lock.yaml",
        "**/Cargo.lock",
        "**/composer.lock",
        "**/Gemfile.lock",
        "**/*.lockb",
        "**/node_modules/**",
        "**/bin/**",
        "**/obj/**",
        "**/dist/**",
        "**/out/**",
        "**/artifacts/**",
        "**/*.min.js",
        "**/*.min.css",
        "**/*.map",
    ];
}

public sealed class ContextException(string operation, string detail)
    : Exception($"git {operation}: {detail}");

/// <summary>
/// Produces the per-file diffs the pure <see cref="DiffShaper"/> then budgets. All git, no rules:
/// which files gate, what gets elided, what a reviewer reads — none of that is decided here.
/// </summary>
public sealed class ContextAssembler(IProcessLauncher launcher)
{
    public async Task<IReadOnlyList<FileDiff>> CollectAsync(
        string repoPath,
        string baseRef,
        string sha,
        IReadOnlyList<string>? exclusions = null,
        CancellationToken ct = default)
    {
        var excludes = (exclusions ?? DiffExclusions.Default).Select(e => $":(exclude,glob){e}").ToArray();

        // --numstat: "added deleted path", binaries as "- - path". One call decides binary-ness
        // and file order; each text file then rides its own diff so elision stays whole-file.
        var numstat = await Git(repoPath, ct, ["diff", "--numstat", $"{baseRef}..{sha}", "--", ".", .. excludes]);

        var files = new List<FileDiff>();
        foreach (var line in numstat.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 3)
            {
                continue;
            }

            var path = parts[2];
            if (parts[0] == "-")
            {
                files.Add(new FileDiff(path, string.Empty, IsBinary: true, BinaryBytes: await BlobSize(repoPath, sha, baseRef, path, ct)));
            }
            else
            {
                var text = await Git(repoPath, ct, ["diff", $"{baseRef}..{sha}", "--", path]);
                files.Add(new FileDiff(path, text));
            }
        }

        return files;
    }

    private async Task<long> BlobSize(string repoPath, string sha, string baseRef, string path, CancellationToken ct)
    {
        foreach (var rev in (string[])[sha, baseRef])
        {
            var result = await launcher.RunAsync(
                new ProcessRequest("git", ["cat-file", "-s", $"{rev}:{path}"], repoPath), ct);
            if (result.ExitCode == 0 && long.TryParse(result.StdOut.Trim(), out var size))
            {
                return size;
            }
        }

        return 0; // deleted on both sides of a rename chain — the name still reaches the reviewer
    }

    private async Task<string> Git(string repoPath, CancellationToken ct, string[] args)
    {
        var result = await launcher.RunAsync(new ProcessRequest("git", args, repoPath), ct);
        return result.ExitCode == 0
            ? result.StdOut
            : throw new ContextException(args[0], result.StdErr.Trim());
    }
}
