namespace CoaiMcp.Install;

/// <summary>
/// Codex's <c>[mcp_servers.coai]</c> in <c>${CODEX_HOME:-$HOME/.codex}/config.toml</c>, changed ONLY by <c>codex mcp add</c> / <c>codex mcp remove</c>.
/// </summary>
/// <remarks>
/// <para>Measured 2026-11-22 on codex-cli 0.171.0: <c>codex mcp add</c> REPLACES an existing entry — its old <c>env</c> and <c>startup_timeout_sec</c> are lost — and drops an inline comment on another
/// server's line. So an update passes the old env again (<c>--env K=V</c>, one per name) and the old arguments, and the file is READ by this repository's own strict TOML reader
/// (<see cref="TomlSubset"/>), so the run asks the vendor nothing until it has a change to make. The values in <c>--env</c> are in the vendor child's argv for its lifetime — there is no
/// stdin form — which is why the output and every sentence here carry names only.</para>
/// </remarks>
internal sealed class CodexMcp : McpTarget
{
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

    protected override Task<VendorAnswer> AddAsync(McpEntry entry, ApplyContext context, CancellationToken ct)
    {
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
