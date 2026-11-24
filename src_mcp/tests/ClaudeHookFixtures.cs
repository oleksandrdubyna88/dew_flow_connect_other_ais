using System.Text.Json.Nodes;

namespace CoaiMcp.Tests;

/// <summary>The settings.json of a person who has used Claude Code, and the readings every hook scenario makes of one.</summary>
internal static class ClaudeHookFixtures
{
    /// <summary>
    /// Another tool's hooks and keys the installer must leave exactly as they are: the <c>Agent|Task</c> guard, a SessionStart hook of the conventions, the agent-models plugin folder, permissions, an effort
    /// key, and the three spellings a re-serialising writer would change — a non-ASCII letter, a <c>&lt;</c>, and <c>1e5</c>.
    /// </summary>
    internal const string Lived = """
        {
          "model": "opus",
          "env": {
            "CLAUDE_CODE_PLUGIN_DIRS": "/home/p/.claude/mods/agent-models",
            "NOTE": "é<1e5>"
          },
          "permissions": { "allow": ["Bash(ls:*)"], "defaultMode": "acceptEdits" },
          "effortLevel": "high",
          "ratio": 1e5,
          "hooks": {
            "PreToolUse": [
              { "matcher": "Agent|Task", "hooks": [ { "type": "command", "command": "/home/p/.claude/hooks/guard.mjs", "args": ["--strict"] } ] }
            ],
            "SessionStart": [
              { "hooks": [ { "type": "command", "command": "node", "args": ["/home/p/.claude/hooks/ensure-user-guard.mjs"] } ] }
            ]
          }
        }
        """;

    /// <summary>The same keys with <c>hooks</c> but no <c>SessionStart</c>.</summary>
    internal const string LivedWithoutSessionStart = """
        {
          "model": "opus",
          "hooks": {
            "PreToolUse": [
              { "matcher": "Agent|Task", "hooks": [ { "type": "command", "command": "/home/p/.claude/hooks/guard.mjs" } ] }
            ]
          },
          "ratio": 1e5
        }
        """;

    /// <summary>Keys but no <c>hooks</c> at all.</summary>
    internal const string LivedWithoutHooks = """
        {
          "model": "opus",
          "env": { "NOTE": "é<1e5>" },
          "ratio": 1e5
        }
        """;

    /// <summary>The group of ours in a settings text, or none: the one whose handler starts a <c>coai-mcp</c> with <c>--roles</c>.</summary>
    internal static JsonNode? OurGroup(string settings) =>
        JsonNode.Parse(settings)?["hooks"]?["SessionStart"]?.AsArray().FirstOrDefault(group => Handlers(group).Any(IsOurs));

    internal static IEnumerable<JsonNode> Handlers(JsonNode? group) => group?["hooks"] is JsonArray list ? list.OfType<JsonNode>() : [];

    internal static bool IsOurs(JsonNode handler) =>
        handler["command"]?.GetValue<string>() is { } command
        && Path.GetFileName(command.Replace('\\', '/')) is "coai-mcp" or "coai-mcp.exe"
        && handler["args"]?[0]?.GetValue<string>() == "--roles";

    /// <summary>Every handler of ours anywhere under <c>hooks.SessionStart</c>.</summary>
    internal static IReadOnlyList<JsonNode> OurHandlers(string settings) =>
        [.. (JsonNode.Parse(settings)?["hooks"]?["SessionStart"] as JsonArray ?? []).SelectMany(Handlers).Where(IsOurs)];

    /// <summary>The settings with our handler's group taken out, and the containers that held only it — what the file was before the install, as a document.</summary>
    internal static JsonNode WithoutOurs(string settings)
    {
        var root = JsonNode.Parse(settings)!.AsObject();
        if (root["hooks"] is JsonObject hooks && hooks["SessionStart"] is JsonArray groups)
        {
            foreach (var group in groups.Where(group => Handlers(group).Any(IsOurs)).ToList())
            {
                groups.Remove(group);
            }

            if (groups.Count == 0)
            {
                hooks.Remove("SessionStart");
            }

            if (hooks.Count == 0)
            {
                root.Remove("hooks");
            }
        }

        return root;
    }

    /// <summary>
    /// "The text diff is added lines only": every line of <paramref name="before"/> is still in <paramref name="after"/>, in the same order, and nothing between them was changed. Returns the lines that are new.
    /// </summary>
    internal static IReadOnlyList<string> AddedLines(string before, string after)
    {
        var old = before.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var now = after.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var added = new List<string>();
        var at = 0;
        foreach (var line in now)
        {
            if (at < old.Length && line == old[at])
            {
                at++;
            }
            else
            {
                added.Add(line);
            }
        }

        return at == old.Length ? added : throw new InvalidOperationException($"a line of the original is missing or changed: '{old[at]}'");
    }
}
