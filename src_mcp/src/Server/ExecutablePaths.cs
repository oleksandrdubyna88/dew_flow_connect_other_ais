namespace CoaiMcp.Server;

/// <summary>What this side does with a stored CLI path: the path it runs (empty = the PATH lookup), and the value it skipped.</summary>
/// <param name="Path">The trimmed path this side runs, or empty when the runtime's own name is looked up on PATH.</param>
/// <param name="OtherSide">The trimmed stored value when it was spelled for the other operating system — empty otherwise.</param>
public sealed record ThisSidePath(string Path, string OtherSide);

/// <summary>
/// A CLI path spelled for the OTHER operating system is the other side's CLI: skipped here, never probed, never refused
/// (todo/PLAN_paths_per_side.md E1.2).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> VS Code's user settings are shared by a WSL window and a plain Windows window on one machine, and each
/// side runs its own coai-mcp. A reviewer row's <c>C:\…\codex.cmd</c> written from Windows reached the WSL server, whose
/// probe answered <c>CliFound=false</c>; the card said "cannot review" and every round on that side lost the vendor —
/// for a row that is right on the side that wrote it.</para>
/// <para>The extension already writes THIS side's value into <c>COAI_VENDORS</c>; this is defence in depth for a settings
/// file an older extension wrote. The spelling alone decides (<see cref="QuestionRoots.OtherSide"/>) — a CLI path is not
/// asked of the disk — and <c>shared/path-family-vectors.json</c>'s <c>executable</c> set is answered by this and by the
/// extension's <c>pathForThisSide</c>.</para>
/// </remarks>
public static class ExecutablePaths
{
    /// <summary>The path this side runs for a stored <paramref name="path"/>, on a host that is or is not Windows.</summary>
    public static ThisSidePath Here(string? path, bool windows)
    {
        var value = path?.Trim() ?? string.Empty;

        return QuestionRoots.OtherSide(value, windows)
            ? new ThisSidePath(string.Empty, value)
            : new ThisSidePath(value, string.Empty);
    }

    /// <summary>The Information line that says a row's CLI path was the other side's — the operator's terminal reads it.</summary>
    public static string SkippedSentence(string provider, string path, bool windows) => windows
        ? $"COAI_VENDORS: {provider}'s CLI path '{path}' is a Linux, WSL or macOS path and this server runs on Windows — it is the other side's CLI, run by the server there; this side skips it and looks the CLI up on PATH"
        : $"COAI_VENDORS: {provider}'s CLI path '{path}' is a Windows path and this server does not run on Windows — it is the Windows side's CLI, run by the server there; this side skips it and looks the CLI up on PATH";
}
