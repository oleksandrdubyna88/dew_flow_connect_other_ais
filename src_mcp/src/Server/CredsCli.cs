namespace CoaiMcp.Server;

/// <summary>
/// Where the <c>creds</c> CLI can be, beyond PATH: the folder the CredsForDevs VS Code extension
/// installs it into (PLAN_feature_review.md §9.10).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> CredsForDevs does not put its CLI on PATH. It downloads it into its
/// own global storage — <c>&lt;editor data&gt;/User/globalStorage/remsoftdev.creds-for-devs/bin/creds[.exe]</c>
/// — and the folder it does add to PATH (<c>%LOCALAPPDATA%\Programs\creds</c> on Windows) holds only
/// <c>creds-mcp.exe</c>. Measured 2026-09-26 by S0.5's probe: a server VS Code started answered
/// "the `creds` CLI is not installed" on a machine where the CLI was installed, so no <c>api</c>
/// vendor got a key.</para>
/// <para><b>Why here, and not a path the extension passes.</b> The server is started by whatever MCP
/// client the person uses — Claude Code, Codex, a script — and three of its modes read the vault
/// (<c>serve</c>, <c>--providers</c>, <c>--probe-api</c>). A path handed over by the coai extension
/// would reach only the launches that extension controls, and only once both halves had shipped;
/// looking in the extension's well-known folder reaches all three modes from any client, in one
/// place, with no new wire field.</para>
/// <para><b>What it does NOT reach.</b> CredsForDevs is a UI extension (<c>extensionKind: ["ui"]</c>),
/// so its CLI lives on the machine the editor window runs on. A server running inside WSL or on a
/// remote host looks in THAT machine's folders and will not find a Windows <c>creds.exe</c>; PATH is
/// still the answer there.</para>
/// <para>The layouts are VS Code's own user-data folders — <c>%APPDATA%</c> on Windows,
/// <c>~/Library/Application Support</c> on macOS, <c>$XDG_CONFIG_HOME</c> (else <c>~/.config</c>) on
/// Linux — for the stable and the Insiders build.</para>
/// </remarks>
public static class CredsCli
{
    /// <summary>The name PATH is asked for first.</summary>
    public const string OnPath = "creds";

    /// <summary>The CredsForDevs extension's id — its global-storage folder is named after it.</summary>
    public const string ExtensionId = "remsoftdev.creds-for-devs";

    /// <summary>Which operating system's folder layout to use. A parameter, so every layout is a unit test on any machine.</summary>
    public enum HostOs
    {
        Windows,
        MacOs,
        Linux,
    }

    /// <summary>The VS Code builds whose user-data folder may hold the extension.</summary>
    private static readonly string[] Editors = ["Code", "Code - Insiders"];

    /// <summary>The OS this process runs on.</summary>
    public static HostOs ThisOs =>
        OperatingSystem.IsWindows() ? HostOs.Windows : OperatingSystem.IsMacOS() ? HostOs.MacOs : HostOs.Linux;

    /// <summary>
    /// Every place the extension would have put the CLI on <paramref name="os"/>, present or not.
    /// </summary>
    /// <remarks>
    /// A base folder the environment does not name gives no place at all — never a relative path, which
    /// would be resolved against whatever directory the server happened to start in.
    /// </remarks>
    public static IReadOnlyList<string> InstalledByTheExtension(Func<string, string?> env, HostOs os)
    {
        if (UserDataRoot(env, os) is not { Length: > 0 } root)
        {
            return [];
        }

        var binary = os == HostOs.Windows ? "creds.exe" : "creds";

        return [.. Editors.Select(editor => Path.Combine(root, editor, "User", "globalStorage", ExtensionId, "bin", binary))];
    }

    /// <summary>The places from <see cref="InstalledByTheExtension"/> that exist, in order.</summary>
    public static IReadOnlyList<string> Present(Func<string, string?> env, HostOs os, Func<string, bool> exists) =>
        [.. InstalledByTheExtension(env, os).Where(exists)];

    /// <summary>Where VS Code keeps its per-user data on this OS, or empty when the environment does not say.</summary>
    private static string UserDataRoot(Func<string, string?> env, HostOs os) => os switch
    {
        HostOs.Windows => Named(env("APPDATA")),
        HostOs.MacOs => Under(env("HOME"), "Library", "Application Support"),
        _ => Named(env("XDG_CONFIG_HOME")) is { Length: > 0 } xdg ? xdg : Under(env("HOME"), ".config"),
    };

    private static string Named(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string Under(string? home, params string[] parts) =>
        Named(home) is { Length: > 0 } root ? Path.Combine([root, .. parts]) : string.Empty;
}
