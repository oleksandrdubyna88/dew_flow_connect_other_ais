using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Install;

namespace CoaiMcp.Tests;

/// <summary>Reading the installer's one JSON object, and the fixtures the scenarios share.</summary>
internal static class InstallOutput
{
    /// <summary>A client of the output by its word.</summary>
    internal static JsonElement Client(this JsonElement output, string cli) =>
        output.GetProperty("clients").EnumerateArray().Single(one => one.GetProperty("cli").GetString() == cli);

    internal static string Result(this JsonElement output, string cli) => output.Client(cli).GetProperty("result").GetString()!;

    /// <summary>The MCP target of a client; the hook, which Claude also has, is asked for by its kind.</summary>
    internal static JsonElement Target(this JsonElement output, string cli, string kind = "mcp") =>
        output.Client(cli).GetProperty("targets").EnumerateArray().Single(one => one.GetProperty("kind").GetString() == kind);

    internal static string Action(this JsonElement output, string cli, string kind = "mcp") => output.Target(cli, kind).GetProperty("action").GetString()!;

    /// <summary>The <c>coai</c> entry of a <c>.claude.json</c> text.</summary>
    internal static JsonNode? ClaudeEntry(string text) => JsonNode.Parse(text)?["mcpServers"]?["coai"];

    /// <summary>A text with no <c>coai</c> entry: what must equal the file before, whatever the installer did.</summary>
    internal static string WithoutOurs(string claudeJson)
    {
        var root = JsonNode.Parse(claudeJson)!.AsObject();
        root["mcpServers"]?.AsObject().Remove("coai");

        return root.ToJsonString();
    }

    internal static string Compact(string json) => JsonNode.Parse(json)!.ToJsonString();

    /// <summary>A <c>.claude.json</c> of a person who has used Claude Code: counters, another server with a secret, a project.</summary>
    internal const string LivedInClaudeJson = """
        {
          "numStartups": 7,
          "theme": "dark",
          "mcpServers": {
            "other": { "type": "stdio", "command": "/usr/bin/other", "args": ["-x", "é<1e5>"], "env": { "TOKEN": "s3cr3t-value" } }
          },
          "projects": { "/repo": { "allowedTools": [], "mcpServers": { "x": { "type": "stdio", "command": "y" } } } }
        }
        """;

    /// <summary>A <c>config.toml</c> with a model, another server with its own env, and a comment.</summary>
    internal const string LivedInCodexToml = """
        model = "gpt-6"  # the owner's choice

        [mcp_servers.other]
        command = "/usr/bin/other"
        args = ["-y", "é"]
        startup_timeout_sec = 20

        [mcp_servers.other.env]
        KEY = "value-4711"
        """;

    /// <summary>The same document by its parsed form, which is what "unchanged" means for TOML: spacing and comments are not content.</summary>
    internal static bool SameToml(string a, string b) =>
        TomlSubset.Parse(a) is { Root: { } left } && TomlSubset.Parse(b) is { Root: { } right } && Equal(left, right);

    private static bool Equal(object? a, object? b) => (a, b) switch
    {
        (Dictionary<string, object> x, Dictionary<string, object> y) => x.Count == y.Count && x.All(pair => y.TryGetValue(pair.Key, out var other) && Equal(pair.Value, other)),
        (List<object> x, List<object> y) => x.Count == y.Count && x.Zip(y).All(pair => Equal(pair.First, pair.Second)),
        _ => Equals(a, b),
    };

    /// <summary>A world whose files already hold an old <c>coai</c> entry at a path that is not there, with the environment the extension writes.</summary>
    internal static void WithOldEntries(InstallWorld world, string old = "/old/coai/versions/1.0.0/coai-mcp")
    {
        world.Put(world.ClaudeJson, $$"""
            { "numStartups": 3, "mcpServers": { "coai": { "type": "stdio", "command": "{{old}}", "args": ["--x"], "env": { "COAI_DATA_DIR": "/data/coai", "COAI_DATA_SIDE": "wsl" } } } }
            """);
        world.Put(world.CodexToml, $"""
            [mcp_servers.coai]
            command = "{old}"
            args = ["--x"]

            [mcp_servers.coai.env]
            COAI_DATA_DIR = "/data/coai"
            COAI_DATA_SIDE = "wsl"
            """);
    }
}
