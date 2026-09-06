namespace CoaiServer;

/// <summary>
/// The environment variables that make one CLI launch use one account and see no other.
/// </summary>
/// <remarks>
/// <para>Pure, and returning a dictionary rather than mutating anything, so the whole isolation
/// contract is one testable value. It goes into <c>ProcessRequest.Environment</c> — never argv and
/// never a log line, the rule every runtime here already keeps for credentials.</para>
/// <para><b>Why more than <c>HOME</c>.</b> The first draft redirected <c>HOME</c> plus each CLI's own
/// variable, and the plan round called that Blocking: plenty of tooling stores state under
/// <c>$XDG_CONFIG_HOME</c> / <c>$XDG_DATA_HOME</c> rather than under <c>$HOME</c> directly, and those
/// are read from the AMBIENT environment when unset — so two slots would share one config directory
/// and the second sign-in would overwrite the first. Redirecting them costs two dictionary entries
/// and removes a whole class of "the accounts keep logging each other out" that would have been
/// diagnosed for days.</para>
/// <para>They are set to paths INSIDE the slot rather than cleared, because clearing them means
/// "fall back to <c>$HOME/.config</c>", which is the slot's HOME anyway on a good day and the
/// container's on a bad one. Naming them is unambiguous.</para>
/// </remarks>
public static class SlotEnvironment
{
    /// <summary>The variables to launch <paramref name="runtime"/> with for the account in <paramref name="slotDir"/>.</summary>
    /// <param name="token">
    /// The contents of the slot's token file, or empty when there is none. The VALUE rather than a
    /// "does it exist" flag, so this method stays pure — the caller touches the filesystem, this
    /// decides what the launch looks like — and so the variable is never set to nothing.
    /// </param>
    public static IReadOnlyDictionary<string, string?> For(string runtime, string slotDir, string token = "")
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["HOME"] = slotDir,
            ["XDG_CONFIG_HOME"] = Path.Combine(slotDir, ".config"),
            ["XDG_DATA_HOME"] = Path.Combine(slotDir, ".local", "share"),
            ["XDG_CACHE_HOME"] = Path.Combine(slotDir, ".cache"),
            ["XDG_STATE_HOME"] = Path.Combine(slotDir, ".local", "state"),
        };

        foreach (var (name, value) in Specific(runtime, slotDir, token))
        {
            env[name] = value;
        }

        return env;
    }

    /// <summary>The name of the token file this runtime can be handed instead of a sign-in.</summary>
    public static string TokenFileName(string runtime) =>
        runtime.Equals("claude", StringComparison.OrdinalIgnoreCase) ? "claude.token" : string.Empty;

    /// <summary>What each CLI wants beyond the generic redirects.</summary>
    /// <remarks>
    /// <c>antigravity</c> has nothing to add: it reads <c>~/.gemini/antigravity-cli/</c> and has no
    /// directory variable of its own (Google's issue #632 is open), which is exactly why the generic
    /// redirects above had to be right — for that CLI they are the entire isolation.
    /// </remarks>
    private static IEnumerable<KeyValuePair<string, string?>> Specific(
        string runtime, string slotDir, string token) =>
        runtime.ToLowerInvariant() switch
        {
            "codex" => [new("CODEX_HOME", Path.Combine(slotDir, ".codex"))],
            "claude" => ClaudeVars(slotDir, token),
            _ => [],
        };

    private static IEnumerable<KeyValuePair<string, string?>> ClaudeVars(string slotDir, string token)
    {
        yield return new("CLAUDE_CONFIG_DIR", Path.Combine(slotDir, ".claude"));

        if (token.Length > 0)
        {
            // A long-lived token from `claude setup-token` (an `sk-ant-oat01-…`, good for a year),
            // handed over as a variable so the slot needs no interactive sign-in at all. The caller
            // read the file; this method never opens one.
            yield return new("CLAUDE_CODE_OAUTH_TOKEN", token);
        }
    }
}
