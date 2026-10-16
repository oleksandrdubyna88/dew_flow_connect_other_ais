using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// <c>rg</c> and <c>fd</c> are FOUND before a Kimi launch, or the launch is refused — never admitted into the owned home
/// (D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; E4.2 decision 2, deviation 11).
/// </summary>
/// <remarks>
/// <para>Kimi's <c>Grep</c> and <c>Glob</c> download ripgrep and fd into <c>$KIMI_CODE_HOME/bin</c> when they are not found, and
/// Moonshot's installer does not ship them (item 4 of the E4 plan's "disagree" list). Admitting a <c>bin/rg.exe</c> by a hash coai
/// pins would put code coai never ran in the owned home under a hash KIMI checks, which is the alternative D19 itself rejected.
/// So the owned home never holds code: the tools are found on the person's machine — their own <c>bin</c> (<c>KIMI_INSTALL_DIR</c>
/// else <c>~/.kimi-code</c>, plus <c>/bin</c>) or a directory of the PARENT's <c>PATH</c> — and the launch's <c>PATH</c> is built so
/// that both resolve without a download (<c>KimiEnvironment.BuiltPath</c>). One or both missing is a refusal before any process, with
/// the two documented commands in the sentence.</para>
/// <para>Pure apart from the injected file test, so the whole table is a unit test rather than something discovered on somebody's
/// laptop. Resolution is <see cref="ExecutableResolver"/>'s per-directory probe, fed this module's own candidates (the name, then
/// each <c>PATHEXT</c> suffix) because the resolver's list is fixed and Kimi's tools are found the way a shell would find them.</para>
/// </remarks>
public static class KimiTools
{
    /// <summary>ripgrep's own Windows package — the first of the two commands the Install button runs and the refusal quotes.</summary>
    public const string RipgrepInstall = "winget install --id BurntSushi.ripgrep.MSVC -e";

    /// <summary>fd's own Windows package.</summary>
    public const string FdInstall = "winget install --id sharkdp.fd -e";

    private static readonly string[] Names = ["rg", "fd"];

    private static readonly string[] DefaultExtensions = [".COM", ".EXE", ".BAT", ".CMD"];

    /// <summary>
    /// The directories — never <paramref name="bin"/> itself, each once, <c>rg</c>'s before <c>fd</c>'s — where the tools are found
    /// outside the person's <c>bin</c>: what the built <c>PATH</c> must name after <c>bin</c> so both resolve.
    /// </summary>
    public static IReadOnlyList<string> FoldersFor(bool isWindows, string bin, string? path, string? pathext, Func<string, bool> exists)
    {
        var comparer = isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        return
        [
            .. Names
                .Select(name => FolderOf(isWindows, bin, name, path, pathext, exists))
                .Where(folder => folder.Length > 0 && !comparer.Equals(folder, Trimmed(bin, isWindows)))
                .Distinct(comparer),
        ];
    }

    /// <summary>The refusal naming the missing tool(s) and the two cures, or empty when both are found.</summary>
    public static string Refusal(bool isWindows, string bin, string? path, string? pathext, Func<string, bool> exists)
    {
        var missing = Names.Where(name => FolderOf(isWindows, bin, name, path, pathext, exists).Length == 0).ToList();

        return missing.Count == 0
            ? string.Empty
            : $"{string.Join(" and ", missing)} {(missing.Count == 1 ? "was" : "were")} not found in {bin} or on PATH — Kimi's Grep and Glob would download "
              + $"{(missing.Count == 1 ? "it" : "them")} into the coai-owned home, which coai refuses (D19); install them — `{RipgrepInstall}` and `{FdInstall}`, "
              + "the two steps the panel's Install button for Kimi runs — and restart the editor; or run kimi once in a terminal and use its search, "
              + $"which downloads both into {bin}";
    }

    /// <summary>The first directory — the person's <c>bin</c>, then each entry of <c>PATH</c> — where <paramref name="name"/> resolves, or empty.</summary>
    private static string FolderOf(bool isWindows, string bin, string name, string? path, string? pathext, Func<string, bool> exists)
    {
        var candidates = Candidates(isWindows, name, pathext);

        return Directories(isWindows, bin, path)
            .FirstOrDefault(directory => ExecutableResolver.In(directory, candidates, isWindows, exists).Length > 0)
            ?? string.Empty;
    }

    /// <summary>The name and each <c>PATHEXT</c> suffix on Windows (the platform's defaults when the variable is empty); the name alone elsewhere.</summary>
    private static List<string> Candidates(bool isWindows, string name, string? pathext)
    {
        var extensions = (pathext ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return isWindows
            ? [.. (extensions.Length > 0 ? extensions : DefaultExtensions).Select(extension => name + extension), name]
            : [name];
    }

    /// <summary><paramref name="bin"/> first, then <c>PATH</c>'s entries, each without a trailing separator.</summary>
    private static IEnumerable<string> Directories(bool isWindows, string bin, string? path) =>
        new[] { bin }
            .Concat((path ?? string.Empty).Split(isWindows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(directory => Trimmed(directory, isWindows));

    /// <summary>Without a trailing separator — except a drive root, whose separator is the folder.</summary>
    private static string Trimmed(string directory, bool isWindows) =>
        isWindows && directory.Length == 3 && directory[1] == ':' ? directory : directory.TrimEnd(isWindows ? WindowsSeparators : UnixSeparators);

    private static readonly char[] WindowsSeparators = ['\\', '/'];

    private static readonly char[] UnixSeparators = ['/'];
}
