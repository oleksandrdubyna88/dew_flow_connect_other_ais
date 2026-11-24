using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CoaiMcp;

/// <summary>
/// The v1 names of the tools and the prompt v2 renamed, kept as hidden aliases for older clients (server.md § 2: they go in 2.1).
/// </summary>
/// <remarks>
/// <para>An alias is a real tool registered under the old name: it runs the same code as the new one and adds a
/// <c>note</c> saying what it was renamed to. It is NOT listed — <see cref="HideFromLists"/> removes it from
/// <c>tools/list</c> (and the old prompt from <c>prompts/list</c>) — so a new client sees only the new names, while a
/// client that learned the old ones from its own instructions keeps working. The SDK has no "hidden" switch, but its request
/// filters wrap the whole list, which is where this cuts.</para>
/// <para>The pairs are spelled here once, so the tools, the prompt, the filter and the tests read one table.</para>
/// </remarks>
internal static class RenamedTools
{
    internal const string OldConsult = "consult";

    internal const string OldClose = "close_consult";

    internal const string OldAsk = "ask_consultants";

    /// <summary>Each old tool name and the one that replaced it.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Tools = new Dictionary<string, string>
    {
        [OldConsult] = "cons",
        [OldClose] = "cons_close",
        [OldAsk] = "consask",
    };

    /// <summary>The v1 prompt <c>consult</c>, which is <c>cons</c> now.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Prompts = new Dictionary<string, string>
    {
        [OldConsult] = "cons",
    };

    /// <summary>The note an alias adds to its reply — and the start of its description.</summary>
    internal static string NoteFor(string newName) =>
        $"renamed to {newName}: call {newName} — the old name still answers in 2.0.x and is removed in 2.1";

    /// <summary>What the alias says about itself where a client reads descriptions.</summary>
    internal static string AliasDescription(string newName) =>
        $"{NoteFor(newName)}. Takes the same arguments as {newName} and answers the same way, with a `note` added.";

    /// <summary>
    /// The options of the alias of a tool: everything the new tool declares — title and hints — under the old name, with a
    /// description that says it was renamed. Built from the new tool's options so the two cannot be declared apart.
    /// </summary>
    internal static McpServerToolCreateOptions Alias(McpServerToolCreateOptions renamed, string oldName) => new()
    {
        Name = oldName,
        Title = renamed.Title,
        Description = AliasDescription(renamed.Name!),
        ReadOnly = renamed.ReadOnly,
        Idempotent = renamed.Idempotent,
        Destructive = renamed.Destructive,
        OpenWorld = renamed.OpenWorld,
    };

    /// <summary>
    /// <paramref name="reply"/> with <c>"note": "renamed to …"</c> added to the top-level object — written into the text, so every
    /// other byte of the answer is the one the new tool gives. A reply that is no object, or that already has a <c>note</c>, gets
    /// the sentence under <c>renamed</c> instead of losing either.
    /// </summary>
    internal static string WithNote(string reply, string newName)
    {
        var close = reply.LastIndexOf('}');
        if (close < 0)
        {
            return reply;
        }

        bool hasMembers;
        bool hasNote;
        try
        {
            using var parsed = JsonDocument.Parse(reply);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return reply;
            }

            hasMembers = parsed.RootElement.EnumerateObject().Any();
            hasNote = parsed.RootElement.TryGetProperty("note", out _);
        }
        catch (JsonException)
        {
            return reply;
        }

        var member = $"\"{(hasNote ? "renamed" : "note")}\": \"{JsonEncodedText.Encode(NoteFor(newName))}\"";
        var before = reply[..close].TrimEnd();

        return $"{before}{(hasMembers ? "," : string.Empty)}\n  {member}\n}}";
    }

    /// <summary>Takes the old names out of what a client is told exists. The aliases stay callable.</summary>
    internal static void HideFromLists(McpServerOptions options)
    {
        options.Filters.Request.ListToolsFilters.Add(next => async (context, ct) =>
        {
            var result = await next(context, ct);
            result.Tools = [.. result.Tools.Where(tool => !Tools.ContainsKey(tool.Name))];

            return result;
        });
        options.Filters.Request.ListPromptsFilters.Add(next => async (context, ct) =>
        {
            var result = await next(context, ct);
            result.Prompts = [.. result.Prompts.Where(prompt => !Prompts.ContainsKey(prompt.Name))];

            return result;
        });
    }
}
