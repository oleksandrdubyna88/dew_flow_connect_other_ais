namespace CoaiMcp.Install;

/// <summary>
/// Where each client keeps the files this installer reads or changes, on ONE side (Windows, or WSL/Linux/macOS) — read from an
/// environment that is INJECTED, never from the process's own, so a test names a scratch home and cannot reach the real one.
/// </summary>
/// <remarks>
/// <para>Each side writes only its own files (the contract of <c>v2/epoch-1/e3-server-cleanup-install/stories/s6-install/SPEC.md</c>): the paths here are
/// this side's, spelled with this side's separator, and a Windows path in a file on the WSL side is only ever READ (it is how an
/// old entry from the other side is recognised and updated).</para>
/// <para>The four variables a vendor child is started with (<c>HOME</c>, <c>USERPROFILE</c>, <c>CLAUDE_CONFIG_DIR</c>, <c>CODEX_HOME</c>) are
/// kept as they were given, so <see cref="VendorEnvironment"/> hands a child exactly the world this host describes — an unset one is
/// REMOVED from the child, never inherited from the real environment.</para>
/// </remarks>
internal sealed record InstallHost(
    bool IsWindows,
    string Home,
    string ClaudeConfigDir,
    string CodexHome,
    string LocalAppData,
    string PathVariable,
    IReadOnlyDictionary<string, string?> VendorEnvironment)
{
    private char Separator => IsWindows ? '\\' : '/';

    /// <summary>Claude's user-scope MCP file: <c>${CLAUDE_CONFIG_DIR:-$HOME}/.claude.json</c>.</summary>
    internal string ClaudeJson => Join(ClaudeConfigDir.Length > 0 ? ClaudeConfigDir : Home, ".claude.json");

    /// <summary>Claude's settings file: <c>${CLAUDE_CONFIG_DIR:-$HOME/.claude}/settings.json</c> (the hook's file; s7 writes it).</summary>
    internal string ClaudeSettings => Join(ClaudeConfigDir.Length > 0 ? ClaudeConfigDir : Join(Home, ".claude"), "settings.json");

    /// <summary>Codex's config: <c>${CODEX_HOME:-$HOME/.codex}/config.toml</c>.</summary>
    internal string CodexToml => Join(CodexHome.Length > 0 ? CodexHome : Join(Home, ".codex"), "config.toml");

    /// <summary>agy's MCP file as measured on Linux: <c>$HOME/.gemini/config/mcp_config.json</c>.</summary>
    internal string AgyJson => Join(Home, ".gemini", "config", "mcp_config.json");

    /// <summary>Where backups, manifests and the lock live: <c>%LOCALAPPDATA%\coai</c> or <c>~/.local/state/coai</c> — never the data folder.</summary>
    internal string StateDir => IsWindows ? Join(LocalAppData.Length > 0 ? LocalAppData : Join(Home, "AppData", "Local"), "coai") : Join(Home, ".local", "state", "coai");

    internal string BackupsDir => Join(StateDir, "install-backups");

    internal string LockFile => Join(StateDir, "install.lock");

    /// <summary>The user's own bin folder, where the CLIs' installers put them.</summary>
    internal string LocalBin => Join(Home, ".local", "bin");

    /// <summary>The home as this side spells it, trimmed of a trailing separator — empty when the side has none.</summary>
    internal bool HasHome => Home.Length > 0;

    internal string Join(params string[] parts) =>
        string.Join(Separator, parts.Select((part, index) => index == 0 ? part.TrimEnd(Separator) : part.Trim(Separator)));

    /// <summary>The host of <paramref name="env"/>. Pure: the platform is a parameter, so every side is a unit test on any one of them.</summary>
    internal static InstallHost From(Func<string, string?> env, bool isWindows)
    {
        string Value(string name) => env(name)?.Trim() ?? string.Empty;
        var home = isWindows ? Value("USERPROFILE") : Value("HOME");

        return new InstallHost(
            isWindows,
            home,
            Value("CLAUDE_CONFIG_DIR"),
            Value("CODEX_HOME"),
            Value("LOCALAPPDATA"),
            env(isWindows ? "Path" : "PATH") ?? env("PATH") ?? string.Empty,
            new Dictionary<string, string?>
            {
                ["HOME"] = NullIfEmpty(Value("HOME")),
                ["USERPROFILE"] = NullIfEmpty(Value("USERPROFILE")),
                ["CLAUDE_CONFIG_DIR"] = NullIfEmpty(Value("CLAUDE_CONFIG_DIR")),
                ["CODEX_HOME"] = NullIfEmpty(Value("CODEX_HOME")),
            });
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
