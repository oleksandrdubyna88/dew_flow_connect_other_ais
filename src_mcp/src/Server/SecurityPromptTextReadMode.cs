using System.Text.Json;
using CoaiMcp.Core.Security;

namespace CoaiMcp.Server;

/// <summary>
/// <c>--security-prompt-text --ids a,b</c>: each security prompt's override file as THIS server reads it — the
/// state of the file it would send — printed as one JSON object, and leave.
/// </summary>
/// <remarks>
/// The live half of a contract with two implementations (research/PLAN_the_security_tab_reads_at_a_glance.md, epic 2):
/// the extension's Security lane tab draws each card from its own reading of the same files, and the seam writes real
/// files and compares the two answers, file by file. The shared vectors pin the RULE; this pins the READING — the
/// encoding, the byte-order mark, the size check — which only the real binary can answer for.
/// </remarks>
internal static class SecurityPromptTextReadMode
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var prompts = new RolePrompts(SettingsFile.DataDirFrom(Environment.GetEnvironmentVariable).Path);
        await Console.Out.WriteLineAsync(Answer(prompts, IdsOf(args)));
        return 0;
    }

    /// <summary>What one file counts as: no file, too large, unreadable, or the shared rule over the text the server reads.</summary>
    internal static string StateOf(RolePrompts prompts, string id)
    {
        var path = prompts.FileToWrite(id);
        if (!File.Exists(path)) return "none";
        if (new FileInfo(path).Length > SecurityContext.MaxPromptBytes) return "oversized";
        return Readable(prompts, id);
    }

    private static string Readable(RolePrompts prompts, string id)
    {
        try { return SecurityPromptText.Classify(prompts.Written(id)).ToString().ToLowerInvariant(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "unreadable"; }
    }

    private static string[] IdsOf(string[] args)
    {
        var at = Array.IndexOf(args, "--ids");
        return at >= 0 && at + 1 < args.Length ? args[at + 1].Split(',', StringSplitOptions.RemoveEmptyEntries) : [];
    }

    private static string Answer(RolePrompts prompts, IReadOnlyList<string> ids)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            foreach (var id in ids) json.WriteString(id, StateOf(prompts, id));
            json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
