using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Kimi;

/// <summary>One model block of an owned Kimi home's <c>config.toml</c>: the values Kimi 2.1.1 accepted, and the section that measured them.</summary>
public sealed record KimiModelBlock(string Id, string Provider, string Model, long MaxContextSize, IReadOnlyList<string> Capabilities, string Source);

/// <summary>What reading a model block came to: the block, or the sentence that says why none — a closed union, the <see cref="Files.Hold"/> shape.</summary>
public abstract record BlockRead
{
    private BlockRead()
    {
    }

    public sealed record Found(KimiModelBlock Block) : BlockRead;

    public sealed record Refused(string Reason) : BlockRead;
}

/// <summary>The embedded table, or why there is none — a value, never an exception on the launch path.</summary>
public abstract record BlocksLoad
{
    private BlocksLoad()
    {
    }

    public sealed record Loaded(KimiModelBlocks Blocks) : BlocksLoad;

    public sealed record Broken(string Reason) : BlocksLoad;
}

/// <summary>
/// <c>shared/kimi-model-blocks.json</c>, embedded: the model block coai writes into an owned Kimi home when the person has
/// none of their own for the model a launch names (D10 and D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; E4.2).
/// </summary>
/// <remarks>
/// <para>One entry per CURATED id, in D10's order, the first being the default: <see cref="KimiModels.Curated"/> IS this
/// table's ids, so the list and the blocks cannot drift. Embedded for the reason every shared seed here is — a Native-AOT binary
/// runs where no <c>shared/</c> exists, and a launch must not be written against a table it could not read: a load failure is a
/// refusal of every Kimi launch (<see cref="BlocksLoad.Broken"/>), never an empty table that admits nothing and says so badly.</para>
/// </remarks>
public sealed record KimiModelBlocks(string SeenOn, IReadOnlyList<KimiModelBlock> Blocks)
{
    private const string Resource = "CoaiMcp.Runners.kimi-model-blocks.json";

    /// <summary>The table this build was shipped with, or the sentence that says why it cannot be read.</summary>
    public static BlocksLoad Builtin { get; } = Load();

    /// <summary>A table document, validated — or a refusal naming what is wrong with it.</summary>
    public static BlocksLoad Parse(string json)
    {
        try
        {
            return Validated(JsonSerializer.Deserialize(json, KimiModelBlocksJson.Default.BlocksFile));
        }
        catch (JsonException e)
        {
            return new BlocksLoad.Broken($"shared/kimi-model-blocks.json is not JSON: {e.Message}");
        }
    }

    /// <summary>
    /// The block for <paramref name="id"/>: the PERSON'S own <c>[models."id"]</c> when their config has one — whole, and on
    /// <c>managed:kimi-code</c> — else the pinned entry; an id with neither is refused with the curated list.
    /// </summary>
    /// <remarks>
    /// The person's intent is their own block: a block coai cannot read whole, or one routed through another provider, is a
    /// refusal with the cure and never a silent fall to the pinned table — a home rendered from other values than the ones the
    /// person's own <c>kimi</c> runs on would answer for a model setup nobody chose. Only the four keys named here are read,
    /// through the one table reader, so a malformed line elsewhere in the file is never parsed and never quoted.
    /// </remarks>
    public static BlockRead For(string id, string personsConfigText, KimiModelBlocks pinned)
    {
        var header = $"[models.\"{id}\"]";

        return CodexConfigFile.HasTable(personsConfigText, header) ? Persons(id, personsConfigText, header) : Pinned(id, pinned);
    }

    private static BlockRead Pinned(string id, KimiModelBlocks pinned) =>
        pinned.Blocks.FirstOrDefault(block => block.Id == id) is { } entry
            ? new BlockRead.Found(entry)
            : new BlockRead.Refused(KimiModels.RefusalFor(id, [.. pinned.Blocks.Select(block => block.Id)]));

    private static BlockRead Persons(string id, string text, string header)
    {
        var provider = CodexConfigFile.TableValue(text, header, "provider");
        var model = CodexConfigFile.TableValue(text, header, "model");
        var capabilities = CodexConfigFile.TableStrings(text, header, "capabilities");
        var size = MaxContextSize(CodexConfigFile.TableValue(text, header, "max_context_size"));
        var routed = Routed(id, provider);

        return routed.Length > 0 ? new BlockRead.Refused(routed)
            : Whole(provider, model, capabilities, size) ? new BlockRead.Found(new KimiModelBlock(id, provider, model, size, capabilities, "your config.toml's own block"))
            : new BlockRead.Refused(
                $"your config.toml has a {header} block coai cannot read whole (it reads provider, model, max_context_size and a one-line capabilities array) — fix the block, or remove it and coai's pinned values are used");
    }

    /// <summary>The sentence for a block routed through any provider but the Kimi Code plan's; empty when it is not (or names none).</summary>
    private static string Routed(string id, string provider) =>
        provider.Length == 0 || provider == ManagedProvider
            ? string.Empty
            : $"your config.toml routes '{id}' through '{provider}', which is not the Kimi Code plan — coai sends nothing to it (D10)";

    /// <summary>All four values there: provider, model, a positive context size and a capabilities array.</summary>
    private static bool Whole(string provider, string model, IReadOnlyList<string> capabilities, long size) =>
        new[] { provider.Length, model.Length, capabilities.Count }.All(count => count > 0) && size > 0;

    /// <summary>A bare integer, else 0 — the value reads as missing.</summary>
    private static long MaxContextSize(string text) =>
        long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size) ? size : 0;

    /// <summary>The Kimi Code plan's provider name — the only one a block may be routed through.</summary>
    internal const string ManagedProvider = "managed:kimi-code";

    private static BlocksLoad Validated(BlocksFile? file) =>
        file is { SeenOn.Length: > 0, Blocks.Count: > 0 } && file.Blocks.All(Complete) && Distinct(file.Blocks)
            ? new BlocksLoad.Loaded(new KimiModelBlocks(file.SeenOn, [.. file.Blocks.Select(Block)]))
            : new BlocksLoad.Broken("shared/kimi-model-blocks.json lacks its seenOn version or a complete, distinct block for each curated id");

    private static bool Complete(BlockEntry? entry) =>
        entry is { Id: { Length: > 0 } id, Provider.Length: > 0, Model.Length: > 0, MaxContextSize: > 0, Capabilities.Count: > 0, Source.Length: > 0 }
        && id.StartsWith(KimiModels.PlanPrefix, StringComparison.Ordinal);

    private static bool Distinct(IReadOnlyList<BlockEntry?> blocks) =>
        blocks.Select(entry => entry!.Id).Distinct(StringComparer.Ordinal).Count() == blocks.Count;

    private static KimiModelBlock Block(BlockEntry? entry) =>
        new(entry!.Id!, entry.Provider!, entry.Model!, entry.MaxContextSize ?? 0, [.. entry.Capabilities!.OfType<string>()], entry.Source!);

    private static BlocksLoad Load()
    {
        using var stream = typeof(KimiModelBlocks).Assembly.GetManifestResourceStream(Resource);
        if (stream is null)
        {
            return new BlocksLoad.Broken($"the embedded resource {Resource} is missing — this build cannot write a Kimi home's model block");
        }

        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }
}

/// <summary>The file as JSON spells it. Every field nullable: an omitted one arrives null (doctrine 4a).</summary>
internal sealed record BlocksFile
{
    public string? SeenOn { get; init; }

    public IReadOnlyList<BlockEntry?>? Blocks { get; init; }
}

internal sealed record BlockEntry
{
    public string? Id { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }

    public long? MaxContextSize { get; init; }

    public IReadOnlyList<string?>? Capabilities { get; init; }

    public string? Source { get; init; }
}

/// <summary>Source-generated, so the Native-AOT binary reads the table without reflection.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BlocksFile))]
internal sealed partial class KimiModelBlocksJson : JsonSerializerContext;
