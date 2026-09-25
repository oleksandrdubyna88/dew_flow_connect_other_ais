using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// A reviewer or a consultant starts with NO MCP servers — issue #514, the user's decision of 2026-09-25.
/// </summary>
/// <remarks>
/// <para>Each vendor CLI loads the person's own MCP servers unless told not to. For Claude that list held
/// <c>coai</c> itself, so every Claude reviewer started a serving <c>coai-mcp</c> of its own and killed it
/// on the way out — which the next server start reported as a run that "never finished" (the 26 rows of
/// #514), besides handing a reviewer the gate's own tools. For Codex it held <c>azure-devops</c>, an
/// <c>npx</c> package started for every reviewer.</para>
/// <para>How each CLI is told differs, and so does how sure the switch is:</para>
/// <list type="bullet">
/// <item>Claude: <see cref="ClaudeFlag"/> with no <c>--mcp-config</c> — only servers from a config it was
/// given, and it is given none.</item>
/// <item>Codex: one <c>-c mcp_servers.&lt;name&gt;.enabled=false</c> per server its <c>config.toml</c>
/// declares. Not <c>--ignore-user-config</c>: that also drops the person's model, service tier and the
/// Windows sandbox mode the read-only sandbox needs. Measured on codex-cli 0.156.1:
/// <c>codex mcp list -c mcp_servers.azure-devops.enabled=false</c> says <c>disabled</c>.</item>
/// <item>agy and gemini: no per-launch switch exists, so the servers their global files name are
/// reported at startup (<see cref="GeminiFamilyConfigured"/>) rather than switched off.</item>
/// </list>
/// </remarks>
public static partial class NoMcpServers
{
    /// <summary>The root table Codex declares its MCP servers under.</summary>
    private const string ServersTable = "mcp_servers";

    /// <summary>How long one match may run: the input is a local config file, but a pattern is never unbounded.</summary>
    private const int MatchTimeoutMs = 1000;

    /// <summary>Claude Code: take MCP servers only from <c>--mcp-config</c>, which is never passed.</summary>
    public const string ClaudeFlag = "--strict-mcp-config";

    /// <summary>A TOML key that needs no quotes.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex Bare { get; }

    /// <summary>
    /// A name that can ride argv to codex safely: on Windows codex is an npm <c>.cmd</c> shim, and cmd.exe
    /// reads an unquoted <c>&amp;</c>, <c>|</c>, <c>%</c> or <c>^</c> as part of a command line of its own.
    /// A name outside this set is not passed, which leaves that one server loaded. (gemini, the code round.)
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9 _.'@-]+$", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex Passable { get; }

    /// <summary>One segment of a dotted TOML key, and what follows it.</summary>
    [GeneratedRegex("""\G\s*("(?:[^"\\]|\\.)*"|'[^']*'|[A-Za-z0-9_-]+)\s*(\.|$)""", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex Segment { get; }

    /// <summary>The <c>-c</c> overrides that switch every named Codex MCP server off for one launch.</summary>
    public static IEnumerable<string> CodexArgs(IReadOnlyList<string> names) =>
        names.Where(name => Passable.IsMatch(name)).SelectMany(name => (string[])["-c", $"{ServersTable}.{CodexKey(name)}.enabled=false"]);

    /// <summary>
    /// A server name as a TOML key segment: bare when it can be, else a LITERAL key.
    /// </summary>
    /// <remarks>
    /// Single quotes first, because a double quote inside an argument that reaches cmd.exe through an npm
    /// shim is a re-tokenisation waiting for the wrong input (the codex consultant's own note). A name
    /// holding a single quote can only be a basic string, escaped.
    /// </remarks>
    public static string CodexKey(string name)
    {
        if (Bare.IsMatch(name))
        {
            return name;
        }
        if (!name.Contains('\'', StringComparison.Ordinal))
        {
            return $"'{name}'";
        }

        return "\"" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>The MCP servers Codex would load from <c>$CODEX_HOME/config.toml</c> — none when it cannot say.</summary>
    /// <param name="env">
    /// The environment the LAUNCH will have: on the Team server codex runs as a slot, with the slot's own
    /// <c>CODEX_HOME</c> and <c>HOME</c>, and the config it reads is that home's, not the server's. (Our own code
    /// reviewer.)
    /// </param>
    public static IReadOnlyList<string> CodexConfigured(Func<string, string?> env)
    {
        var path = Path.Combine(
            env("CODEX_HOME") is { Length: > 0 } codexHome ? codexHome : Path.Combine(HomeIn(env), ".codex"), "config.toml");
        try
        {
            return File.Exists(path) ? CodexServerNames(File.ReadAllText(path)) : [];
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Every MCP server a Codex <c>config.toml</c> declares for certain, in order, once each.
    /// </summary>
    /// <remarks>
    /// <para>Only the forms that cannot be anything else: a table <c>[mcp_servers.x]</c> (with its
    /// sub-tables, which are not new servers), an inline table under <c>[mcp_servers]</c>
    /// (<c>x = { … }</c>), and a dotted key at the ROOT (<c>mcp_servers.x.command = …</c>); bare, "basic"
    /// and 'literal' key segments. Not a TOML parser.</para>
    /// <para><b>Unsure means silent, and that is not a style choice.</b> Measured on codex-cli 0.156.1: an
    /// override for a server config.toml does not declare stops codex from starting at all ("failed to load
    /// bootstrap configuration"). A name missed leaves one server loaded; a name invented breaks every Codex
    /// review. So an array table <c>[[…]]</c> or a header this cannot read ends what it knows about the
    /// current table, and nothing is collected until the next header it can read. (Our own code
    /// reviewer.)</para>
    /// </remarks>
    public static IReadOnlyList<string> CodexServerNames(string toml)
    {
        var names = new List<string>();
        IReadOnlyList<string>? table = [];
        foreach (var raw in toml.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith('['))
            {
                table = line.StartsWith("[[", StringComparison.Ordinal) ? null : HeaderPath(line);
            }
            var name = line.StartsWith('[') ? HeaderServer(table) : KeyServer(line, table);
            if (name is { } found && !names.Contains(found, StringComparer.Ordinal))
            {
                names.Add(found);
            }
        }

        return names;
    }

    /// <summary>The server a table header names — <c>[mcp_servers.x]</c> and its sub-tables — or nothing.</summary>
    private static string? HeaderServer(IReadOnlyList<string>? table) =>
        table is [ServersTable, var name, ..] ? name : null;

    /// <summary>
    /// The server a <c>key = value</c> line declares: an inline table under <c>[mcp_servers]</c>, or a dotted
    /// key at the root — or nothing, which is every other line.
    /// </summary>
    private static string? KeyServer(string line, IReadOnlyList<string>? table)
    {
        var equals = line.IndexOf('=', StringComparison.Ordinal);
        if (table is null || equals <= 0 || Keys(line[..equals]) is not { } keys)
        {
            return null;
        }
        var inline = line[(equals + 1)..].TrimStart().StartsWith('{');

        return (table, keys) switch
        {
            ([ServersTable], [var name]) when inline => name,
            ([], [ServersTable, var name]) when inline => name,
            ([], [ServersTable, var name, _, ..]) => name,
            _ => null,
        };
    }

    /// <summary>The servers the agy and gemini CLIs load from their global files — which no launch can switch off.</summary>
    public static IReadOnlyList<string> GeminiFamilyConfigured()
    {
        var gemini = Path.Combine(Home(), ".gemini");

        return [.. new[] { Path.Combine(gemini, "config", "mcp_config.json"), Path.Combine(gemini, "settings.json") }
            .SelectMany(ServersIn)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The keys of a JSON file's <c>mcpServers</c> object — none when it has none, or cannot be read.</summary>
    public static IReadOnlyList<string> JsonServerNames(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("mcpServers", out var servers)
                && servers.ValueKind == JsonValueKind.Object
                ? [.. servers.EnumerateObject().Select(server => server.Name)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<string> ServersIn(string path)
    {
        try
        {
            return File.Exists(path) ? JsonServerNames(File.ReadAllText(path)) : [];
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The home directory the given environment names — <c>HOME</c>, then <c>USERPROFILE</c> — else this process's.</summary>
    private static string HomeIn(Func<string, string?> env)
    {
        if (env("HOME") is { Length: > 0 } unix)
        {
            return unix;
        }

        return env("USERPROFILE") is { Length: > 0 } windows ? windows : Home();
    }

    /// <summary>The key path of a <c>[table]</c> header, or nothing when it is not one.</summary>
    private static IReadOnlyList<string>? HeaderPath(string line)
    {
        var close = line.LastIndexOf(']');

        return close > 0 ? Keys(line[1..close]) : null;
    }

    /// <summary>A dotted key, each segment unquoted — or nothing when the text is not wholly a key.</summary>
    private static IReadOnlyList<string>? Keys(string text)
    {
        var keys = new List<string>();
        var at = 0;
        var trimmed = text.Trim();
        while (at < trimmed.Length && Segment.Match(trimmed, at) is { Success: true } segment)
        {
            keys.Add(Unquoted(segment.Groups[1].Value));
            at = segment.Index + segment.Length;
        }

        return keys.Count > 0 && at == trimmed.Length ? keys : null;
    }

    private static string Unquoted(string segment) => segment switch
    {
        ['\'', .., '\''] => segment[1..^1],
        ['"', .., '"'] => Unescaped(segment[1..^1]),
        _ => segment,
    };

    /// <summary>A TOML basic string's escapes: <c>\"</c>, <c>\\</c>, <c>\uXXXX</c> — and the rest kept as written.</summary>
    private static string Unescaped(string body)
    {
        var text = new StringBuilder(body.Length);
        var at = 0;
        while (at < body.Length)
        {
            at = Escaped(body, at, text) + 1;
        }

        return text.ToString();
    }

    private static int Escaped(string body, int at, StringBuilder text)
    {
        if (body[at] != '\\' || at + 1 >= body.Length)
        {
            text.Append(body[at]);
            return at;
        }
        var next = body[at + 1];
        if (next == 'u' && at + 5 < body.Length && int.TryParse(body.AsSpan(at + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out var code))
        {
            text.Append((char)code);
            return at + 5;
        }
        text.Append(next);

        return at + 1;
    }
}
