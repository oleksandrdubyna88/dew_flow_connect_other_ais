using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CoaiMcp.FakeCli;

/// <summary>
/// A stand-in for the MCP verbs of <c>claude</c> and <c>codex</c>, as MEASURED on 2026-11-22 (claude 2.1.330, codex-cli 0.171.0) — started as
/// <c>FakeCli vendor-mcp claude|codex &lt;the CLI's own arguments&gt;</c> by a wrapper file named <c>claude</c> / <c>codex</c> on a test's PATH.
/// </summary>
/// <remarks>
/// <para>It is deliberately no more forgiving than the real ones (a fake may be stricter and never more permissive): claude's <c>add-json</c> refuses an existing name and a file it cannot parse
/// (leaving a <c>.corrupted</c> copy); codex's <c>add</c> REPLACES an existing entry — its old env lost — and drops an inline comment on a line it rewrites. Every `mcp` call is logged first (a `--version` is the CLI's identity, not a configuration call, and is not), one line, with the
/// four variables the child saw, so a test can prove that nothing ran, that nothing reached a real home, and that <c>mcp get</c> was never asked.</para>
/// <para>Steering by environment, set in the wrapper: <c>FAKECLI_MCP_LOG</c> (the log file); <c>FAKECLI_MCP_ECHO</c> (a failure quotes the arguments it was given); <c>FAKECLI_MCP_FAIL</c> and <c>FAKECLI_MCP_HANG</c> — a comma list of verbs (<c>add-json</c>, <c>add</c>, <c>remove</c>, <c>version</c>),
/// each optionally <c>:n</c> to act only on the first n calls of it.</para>
/// </remarks>
internal static class McpVendor
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static int Run(string which, string[] argv)
    {
        var verb = argv switch
        {
            ["--version"] => "version",
            ["mcp", var v, ..] => v,
            _ => "unknown",
        };
        var calls = verb == "version" ? 1 : Log(which, argv, verb);
        if (Steered("FAKECLI_MCP_HANG", verb, calls))
        {
            Thread.Sleep(120_000);
        }

        if (Steered("FAKECLI_MCP_FAIL", verb, calls))
        {
            // A vendor that quotes what it was handed in its error: FAKECLI_MCP_ECHO=1 makes this one do it, so a test can see whether a value reaches a sentence.
            var quoted = Environment.GetEnvironmentVariable("FAKECLI_MCP_ECHO") == "1" ? $" — arguments: {string.Join(' ', argv)}" : string.Empty;
            Console.Error.WriteLine($"fake {which}: {verb} failed (steered){quoted}");

            return 1;
        }

        return which == "claude" ? Claude(verb, argv) : Codex(verb, argv);
    }

    /// <summary>Appends the call to the log, and answers how many calls of this verb the log now holds (this one included).</summary>
    private static int Log(string which, string[] argv, string verb)
    {
        if (Environment.GetEnvironmentVariable("FAKECLI_MCP_LOG") is not { Length: > 0 } path)
        {
            return 1;
        }

        var line = $"{which} {string.Join(' ', argv)} | HOME={Environment.GetEnvironmentVariable("HOME")} USERPROFILE={Environment.GetEnvironmentVariable("USERPROFILE")} CLAUDE_CONFIG_DIR={Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")} CODEX_HOME={Environment.GetEnvironmentVariable("CODEX_HOME")}";
        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        using (var writer = new StreamWriter(stream))
        {
            writer.WriteLine(line);
        }

        return File.ReadAllLines(path).Count(one => one.StartsWith($"{which} ", StringComparison.Ordinal) && one.StartsWith($"{which} mcp {verb} ", StringComparison.Ordinal));
    }

    private static bool Steered(string variable, string verb, int calls)
    {
        foreach (var item in (Environment.GetEnvironmentVariable(variable) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split(':');
            if ((parts[0] == verb || parts[0] == "*") && (parts.Length == 1 || calls <= int.Parse(parts[1])))
            {
                return true;
            }
        }

        return false;
    }

    // ---------- claude ----------

    private static string ClaudeFile() =>
        Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } dir ? dir : Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE")!, ".claude.json");

    private static int Claude(string verb, string[] argv)
    {
        switch (verb)
        {
            case "version":
                Console.Out.WriteLine("2.1.330 (Claude Code)");
                return 0;
            case "add-json":
                return ClaudeAdd(argv);
            case "remove":
                return ClaudeRemove(argv[^1]);
            case "get" or "list":
                Console.Out.WriteLine("coai: Connected");
                return 0;
            default:
                Console.Error.WriteLine($"fake claude: no such verb '{verb}'");
                return 1;
        }
    }

    private static JsonObject? ReadClaude(string file, out bool corrupted)
    {
        corrupted = false;
        if (!File.Exists(file))
        {
            return new JsonObject();
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(file)) is JsonObject root)
            {
                return root;
            }
        }
        catch (JsonException)
        {
        }

        corrupted = true;
        File.Copy(file, $"{file}.corrupted.{DateTime.UtcNow.Ticks}");

        return null;
    }

    private static int ClaudeAdd(string[] argv)
    {
        var rest = argv.SkipWhile(one => one != "add-json").Skip(1).ToList();
        var scope = rest.IndexOf("--scope");
        if (scope >= 0)
        {
            rest.RemoveRange(scope, 2);
        }

        var name = rest[0];
        var json = rest[1] == "-" ? Console.In.ReadToEnd() : rest[1];
        var file = ClaudeFile();
        if (ReadClaude(file, out _) is not { } root)
        {
            Console.Error.WriteLine($"fake claude: {file} is corrupted; a copy was kept");

            return 1;
        }

        var servers = root["mcpServers"] as JsonObject ?? new JsonObject();
        if (servers.ContainsKey(name))
        {
            Console.Error.WriteLine($"MCP server {name} already exists in user config");

            return 1;
        }

        servers[name] = JsonNode.Parse(json);
        root["mcpServers"] = servers;
        Write(file, root.ToJsonString(Indented));

        return 0;
    }

    private static int ClaudeRemove(string name)
    {
        var file = ClaudeFile();
        if (ReadClaude(file, out _) is not { } root)
        {
            return 1;
        }

        if (root["mcpServers"] is not JsonObject servers || !servers.Remove(name))
        {
            Console.Error.WriteLine($"No user-scope MCP server found with name: {name}");

            return 1;
        }

        Write(file, root.ToJsonString(Indented));

        return 0;
    }

    private static void Write(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    // ---------- codex ----------

    private static string CodexFile() =>
        Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home ? home : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE")!, ".codex"), "config.toml");

    private static int Codex(string verb, string[] argv)
    {
        switch (verb)
        {
            case "version":
                Console.Out.WriteLine("codex-cli 0.171.0");
                return 0;
            case "add":
                return CodexAdd(argv.Skip(2).ToList());
            case "remove":
                return CodexRemove(argv[2]);
            case "get":
                Console.Out.WriteLine("""{"name":"coai"}""");
                return 0;
            default:
                Console.Error.WriteLine($"fake codex: no such verb '{verb}'");
                return 1;
        }
    }

    private static int CodexAdd(List<string> rest)
    {
        var env = new List<(string, string)>();
        while (rest.Count > 0 && rest[0] == "--env")
        {
            var pair = rest[1].Split('=', 2);
            env.Add((pair[0], pair[1]));
            rest.RemoveRange(0, 2);
        }

        var name = rest[0];
        var command = rest[2];
        var args = rest.Skip(3).ToList();
        var file = CodexFile();
        var text = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
        if (!Sane(text))
        {
            Console.Error.WriteLine($"Error: failed to parse {file}");

            return 1;
        }

        var block = new StringBuilder().Append($"[mcp_servers.{name}]\ncommand = {Quote(command)}\n");
        if (args.Count > 0)
        {
            block.Append($"args = [{string.Join(", ", args.Select(Quote))}]\n");
        }

        if (env.Count > 0)
        {
            block.Append($"\n[mcp_servers.{name}.env]\n").Append(string.Concat(env.Select(pair => $"{pair.Item1} = {Quote(pair.Item2)}\n")));
        }

        var kept = StripComments(WithoutServer(text, name)).TrimEnd();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, (kept.Length > 0 ? kept + "\n\n" : string.Empty) + block, new UTF8Encoding(false));

        return 0;
    }

    private static int CodexRemove(string name)
    {
        var file = CodexFile();
        var text = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
        var without = WithoutServer(text, name);
        if (without == text || !Sane(text))
        {
            Console.Error.WriteLine($"No MCP server named '{name}' found.");

            return 1;
        }

        File.WriteAllText(file, StripComments(without).TrimEnd() + "\n", new UTF8Encoding(false));

        return 0;
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The text without the tables of <paramref name="name"/> — a header line starts a block, which runs to the next header.</summary>
    private static string WithoutServer(string text, string name)
    {
        var keep = new StringBuilder();
        var dropping = false;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('['))
            {
                dropping = Regex.IsMatch(trimmed, $@"^\[mcp_servers\.(""{Regex.Escape(name)}""|{Regex.Escape(name)})(\.[^\]]*)?\]");
            }

            if (!dropping)
            {
                keep.Append(line).Append('\n');
            }
        }

        return keep.ToString().TrimEnd('\n') + (text.EndsWith('\n') ? "\n" : string.Empty);
    }

    /// <summary>A rewrite loses the inline comment after a plain <c>key = "value"</c> line — measured on codex-cli 0.171.0.</summary>
    private static string StripComments(string text) =>
        Regex.Replace(text, "^(\\s*[A-Za-z0-9_.-]+\\s*=\\s*(?:\"[^\"\\n]*\"|[^#\\n\"']*?))\\s+#[^\\n]*$", "$1", RegexOptions.Multiline);

    /// <summary>A coarse "does the real codex parse this": headers close, lines are keys or the inside of an open bracket.</summary>
    private static bool Sane(string text)
    {
        var depth = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || depth > 0)
            {
                depth += line.Count(c => c == '[') - line.Count(c => c == ']');
                if (depth < 0)
                {
                    return false;
                }

                continue;
            }

            var bare = Regex.Replace(line, "\\s+#.*$", string.Empty);
            if (bare.StartsWith('['))
            {
                if (!bare.EndsWith(']'))
                {
                    return false;
                }
            }
            else if (!bare.Contains('='))
            {
                return false;
            }
            else
            {
                depth = Math.Max(0, bare.Count(c => c == '[') - bare.Count(c => c == ']'));
            }
        }

        return depth == 0;
    }
}
