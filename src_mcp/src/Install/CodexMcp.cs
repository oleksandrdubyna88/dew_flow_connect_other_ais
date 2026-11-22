namespace CoaiMcp.Install;

/// <summary>
/// Codex's <c>[mcp_servers.coai]</c> in <c>${CODEX_HOME:-$HOME/.codex}/config.toml</c>, changed ONLY by <c>codex mcp add</c> / <c>codex mcp remove</c>.
/// </summary>
/// <remarks>
/// <para>Measured 2026-11-22 on codex-cli 0.171.0: <c>codex mcp add</c> REPLACES an existing entry — its old <c>env</c> and <c>startup_timeout_sec</c> are lost — and drops an inline comment on another
/// server's line. So an update passes the old env again (<c>--env K=V</c>, one per name) and the old arguments (<c>coai -- &lt;path&gt; &lt;args…&gt;</c>; verified on 0.171.0 that
/// every word after the path is stored as <c>args</c>, an empty one and a flag-looking one included), and the file is READ by this repository's own strict TOML reader
/// (<see cref="TomlSubset"/>), so the run asks the vendor nothing until it has a change to make.</para>
/// <para><b>The values in <c>--env</c> are in the vendor child's argv for its lifetime — there is no stdin form — and the project rule is that no secret ever reaches argv.</b> So only
/// <see cref="NonSecretEnv"/> may cross: the names the extension writes into an entry, whose values are a data folder and a side name. An entry with any other key is REFUSED by
/// <see cref="EnvRefusal"/> before any lock or backup, naming the key and never its value; an argument that is not text is refused the same way, never dropped.</para>
/// </remarks>
internal sealed class CodexMcp : McpTarget
{
    /// <summary>The only environment names whose values may be passed to <c>codex mcp add --env</c>: what the extension writes into an entry (<c>pathSettings.ts</c>: the data folder and its side) — a path and a word, no secret.</summary>
    internal static readonly IReadOnlyList<string> NonSecretEnv = ["COAI_DATA_DIR", "COAI_DATA_SIDE"];

    /// <summary>The empty string when every env name of <paramref name="entry"/> may be passed on a command line, else the sentence that says which names stop it (names only, never a value).</summary>
    internal static string EnvRefusal(McpEntry? entry)
    {
        var stopping = entry?.Env.Keys.Where(name => !NonSecretEnv.Contains(name, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList() ?? [];

        return stopping.Count == 0
            ? string.Empty
            : $"the entry holds the environment key{(stopping.Count > 1 ? "s" : string.Empty)} {string.Join(", ", stopping)}, and `codex mcp add` takes a value only on its command line, where every process of this machine can read it; "
              + $"only {string.Join(" and ", NonSecretEnv)} are known not to be secret, so the entry is left as it is — change it by hand";
    }

    public override Client Cli => Client.Codex;

    public override string FileOf(InstallHost host) => host.CodexToml;

    protected override bool RemoveBeforeAdd => false;

    public override ConfigRead Read(string? text) => ReadFile(text);

    /// <summary>The <c>coai</c> entry of a <c>config.toml</c> text. Codex has no project scope for its servers, so nothing shadows it.</summary>
    internal static ConfigRead ReadFile(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ConfigRead.None;
        }

        var parsed = TomlSubset.Parse(text);
        if (parsed.Root is null)
        {
            return new ConfigRead.Unreadable($"it is not valid TOML ({parsed.Error})");
        }

        if (!parsed.Root.TryGetValue("mcp_servers", out var servers))
        {
            return ConfigRead.None;
        }

        if (servers is not Dictionary<string, object> table)
        {
            return new ConfigRead.Unreadable("mcp_servers is not a table");
        }

        if (table.TryGetValue("coai", out var coai) && coai is Dictionary<string, object> held && held.TryGetValue("args", out var args) && args is List<object> words && words.Any(word => word is not string))
        {
            return new ConfigRead.Unreadable("the args of [mcp_servers.coai] hold a value that is not text, which `codex mcp add` could not hand over, so they would be dropped");
        }

        return new ConfigRead.Found(table.TryGetValue("coai", out var entry) ? EntryOf(entry) : null, []);
    }

    private static McpEntry EntryOf(object node)
    {
        if (node is not Dictionary<string, object> body)
        {
            return new McpEntry(string.Empty, [], new Dictionary<string, string>());
        }

        var command = body.TryGetValue("command", out var c) && c is string path ? path : string.Empty;
        var args = body.TryGetValue("args", out var a) && a is List<object> list ? list.OfType<string>().ToList() : [];
        var env = body.TryGetValue("env", out var e) && e is Dictionary<string, object> map
            ? map.ToDictionary(pair => pair.Key, pair => pair.Value as string ?? pair.Value.ToString() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        return new McpEntry(command, args, env);
    }

    public override string Via(PlanAction action) => action switch
    {
        PlanAction.Add or PlanAction.Update or PlanAction.Restore => "codex mcp add",
        PlanAction.Remove => "codex mcp remove",
        _ => string.Empty,
    };

    protected override Task<VendorAnswer> AddAsync(McpEntry entry, ApplyContext context, CancellationToken ct)
    {
        // The plan refuses such an entry first; this is the last line, for the roads that do not go through it (the put-back of an undo): no value of another key is ever an argument.
        if (EnvRefusal(entry) is { Length: > 0 } refusal)
        {
            return Task.FromResult(new VendorAnswer(false, refusal));
        }

        List<string> args = ["mcp", "add"];
        foreach (var (name, value) in entry.Env.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            args.Add("--env");
            args.Add($"{name}={value}");
        }

        args.AddRange(["coai", "--", entry.Command, .. entry.Args]);

        return context.Vendor.RunAsync(context.CliPath, args, string.Empty, ct);
    }

    protected override Task<VendorAnswer> RemoveAsync(ApplyContext context, CancellationToken ct) =>
        context.Vendor.RunAsync(context.CliPath, ["mcp", "remove", "coai"], string.Empty, ct);
}
