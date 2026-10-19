using System.Globalization;
using System.Text.Json;

namespace CoaiMcp.Store;

internal enum LineKind
{
    Other,
    Assistant,
    User,
}

internal enum BlockKind
{
    /// <summary>A <c>tool_use</c> block of the <c>Agent</c> (or <c>Task</c>) tool.</summary>
    AgentCall,

    /// <summary>A <c>tool_use</c> block of a tool in <see cref="WriteTools.Names"/> whose one path property is a string.</summary>
    WriteCall,

    /// <summary>A <c>tool_result</c> block.</summary>
    Result,
}

/// <summary>
/// One block the reader keeps. <see cref="Tool"/> is a name only for a call (an <c>Agent</c> or a write tool), <see cref="Target"/> is
/// a write's path and nothing else of its input, and <see cref="IsError"/> is a result's <c>is_error</c>.
/// </summary>
internal sealed record BlockFacts(BlockKind Kind, string Id, string Tool, string Target, bool IsError);

/// <summary>The shape-gated values of an assistant line (S4-18). <see cref="OffShape"/> counts the PRESENT values that were refused.</summary>
internal readonly record struct MessageValues(bool Synthetic, string Model, string Requested, string Effort, int OffShape)
{
    public static MessageValues SyntheticLine { get; } = new(true, string.Empty, string.Empty, string.Empty, 0);

    public static MessageValues None { get; } = new(false, string.Empty, string.Empty, string.Empty, 0);
}

/// <summary>Everything the reader takes from one line, as plain values: no <see cref="JsonElement"/> outlives the line's document (C4).</summary>
internal sealed record LineFacts(
    LineKind Kind,
    DateTime? Stamp,
    string AgentId,
    bool Sidechain,
    string MessageId,
    MessageValues Values,
    TokenUsage Tokens,
    bool UsageSeen,
    IReadOnlyList<BlockFacts> Blocks)
{
    public static LineFacts Torn { get; } = new(LineKind.Other, null, string.Empty, false, string.Empty, MessageValues.None, new TokenUsage(0, 0, 0, 0), false, []);
}

/// <summary>
/// What is read from a transcript line — §3.4 of the plan and nothing more: <c>type</c>, <c>timestamp</c>, <c>agentId</c>,
/// <c>isSidechain</c>, <c>message.id</c>, <c>message.model</c>, the four usage numbers, <c>requestedModel</c>, <c>effort</c>, a content
/// block's <c>type</c>, a <c>tool_use</c>'s <c>name</c> and <c>id</c> (and, for a write tool, its one path), a <c>tool_result</c>'s
/// <c>tool_use_id</c> and <c>is_error</c>. Never <c>cwd</c>, <c>description</c>, <c>prompt</c>, <c>toolUseResult</c>, a text block, or an
/// object's keys.
/// </summary>
/// <remarks>The line's document is parsed over the enumerator's own bytes and disposed before this returns, so nothing here can hold a
/// stale <see cref="JsonElement"/>; every value that leaves is a string passed through <see cref="TranscriptShapes"/> (or an id), a number
/// or a flag.</remarks>
internal static class TranscriptLine
{
    private const DateTimeStyles Utc = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

    /// <summary>The line's facts, or false for a line that does not parse (a torn line).</summary>
    public static bool TryRead(ReadOnlyMemory<byte> bytes, out LineFacts facts)
    {
        if (!TranscriptLines.TryParse(bytes, out var document))
        {
            facts = LineFacts.Torn;

            return false;
        }

        using (document)
        {
            facts = FactsOf(document.RootElement);
        }

        return true;
    }

    private static LineFacts FactsOf(JsonElement root)
    {
        var header = new LineFacts(
            KindOf(root), StampOf(root), IdOf(root, "agentId"), JsonText.Flag(root, "isSidechain"),
            string.Empty, MessageValues.None, new TokenUsage(0, 0, 0, 0), false, []);
        var message = JsonText.Child(root, "message");

        return header.Kind switch
        {
            LineKind.Assistant => Assistant(header, root, message),
            LineKind.User => header with { Blocks = Blocks(message, ResultBlock) },
            _ => header,
        };
    }

    private static LineFacts Assistant(LineFacts header, JsonElement root, JsonElement message)
    {
        var usage = JsonText.Child(message, "usage");

        return header with
        {
            MessageId = IdOf(message, "id"),
            Values = ValuesOf(root, message),
            Tokens = TokensOf(usage),
            UsageSeen = usage.ValueKind == JsonValueKind.Object,
            Blocks = Blocks(message, CallBlock),
        };
    }

    /// <summary>An id the line carries, kept only on <see cref="TranscriptShapes.Id"/>; off the shape it reads as absent, so the line is
    /// treated as one with no such id (an assistant line with no usable message id is <c>inconsistent transcript</c>, a block with none is skipped).</summary>
    private static string IdOf(JsonElement element, string name) => TranscriptShapes.Admit(JsonText.StringProperty(element, name), TranscriptShapes.IsId).Value;

    private static LineKind KindOf(JsonElement root)
    {
        var type = JsonText.Child(root, "type");
        if (type.ValueKind != JsonValueKind.String)
        {
            return LineKind.Other;
        }

        return type.ValueEquals("assistant") ? LineKind.Assistant : type.ValueEquals("user") ? LineKind.User : LineKind.Other;
    }

    private static DateTime? StampOf(JsonElement root) =>
        DateTime.TryParse(JsonText.StringProperty(root, "timestamp"), CultureInfo.InvariantCulture, Utc, out var stamp) ? stamp : null;

    private static TokenUsage TokensOf(JsonElement usage) => new(
        JsonText.Count(usage, "input_tokens"),
        JsonText.Count(usage, "cache_creation_input_tokens"),
        JsonText.Count(usage, "cache_read_input_tokens"),
        JsonText.Count(usage, "output_tokens"));

    /// <summary>A synthetic line (empty model, or one that begins with <c>&lt;</c>) is tested BEFORE the shape (S4-11).</summary>
    private static MessageValues ValuesOf(JsonElement root, JsonElement message)
    {
        var model = JsonText.StringProperty(message, "model");
        if (model.Length == 0 || model[0] == '<')
        {
            return MessageValues.SyntheticLine;
        }

        var admittedModel = TranscriptShapes.Admit(model, TranscriptShapes.IsModelRef);
        var requested = TranscriptShapes.Admit(JsonText.StringProperty(root, "requestedModel"), TranscriptShapes.IsModelRef);
        var effort = TranscriptShapes.Admit(JsonText.StringProperty(root, "effort"), TranscriptShapes.IsEffort);

        return new MessageValues(false, admittedModel.Value, requested.Value, effort.Value, admittedModel.OffShape + requested.OffShape + effort.OffShape);
    }

    private static List<BlockFacts> Blocks(JsonElement message, Func<JsonElement, IEnumerable<BlockFacts>> pick)
    {
        var content = JsonText.Child(message, "content");

        return content.ValueKind == JsonValueKind.Array ? [.. content.EnumerateArray().SelectMany(pick)] : [];
    }

    /// <summary>A <c>tool_use</c> of the <c>Agent</c>/<c>Task</c> tool or of a write tool with a path; every other block is skipped.</summary>
    private static IEnumerable<BlockFacts> CallBlock(JsonElement block)
    {
        var id = IdOf(block, "id");

        return id.Length > 0 && IsType(block, "tool_use") ? CallOf(id, JsonText.StringProperty(block, "name"), JsonText.Child(block, "input")) : [];
    }

    private static IEnumerable<BlockFacts> CallOf(string id, string name, JsonElement input)
    {
        if (name is "Agent" or "Task")
        {
            return [new BlockFacts(BlockKind.AgentCall, id, name, string.Empty, false)];
        }

        return WriteTools.TryPath(name, input, out var path) ? [new BlockFacts(BlockKind.WriteCall, id, name, path, false)] : [];
    }

    private static IEnumerable<BlockFacts> ResultBlock(JsonElement block)
    {
        var id = IdOf(block, "tool_use_id");

        return IsType(block, "tool_result") && id.Length > 0 ? [new BlockFacts(BlockKind.Result, id, string.Empty, string.Empty, JsonText.Flag(block, "is_error"))] : [];
    }

    private static bool IsType(JsonElement block, string type)
    {
        var value = JsonText.Child(block, "type");

        return value.ValueKind == JsonValueKind.String && value.ValueEquals(type);
    }
}
