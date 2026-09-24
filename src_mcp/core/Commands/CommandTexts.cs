namespace CoaiMcp.Core.Commands;

/// <summary>
/// The words the gate's orders are made of — shipped in <c>shared/commands/*.md</c>, each one overridable.
/// </summary>
/// <param name="Overrides">What a person wrote, by id — read by the server from
/// <c>&lt;dataDir&gt;/prompts/&lt;id&gt;.md</c>. A blank one is no override: a file created before it was
/// written must not hand the caller an empty order, which is <c>RolePrompts</c>' rule too.</param>
/// <remarks>
/// <para><b>Issue #467.</b> The orders were C# literals, so changing a word of one was a release. They are
/// data now, embedded in this assembly the way the role catalog is, and read by the extension's commands
/// page from the same files — one text both halves show cannot drift.</para>
/// <para><b>What is NOT here: the markers.</b> <c>Work AUTONOMOUSLY.</c>,
/// <see cref="GateCommands.ModelOrderMarker"/>, <see cref="GateCommands.GateOrderMarker"/> and
/// <see cref="GateCommands.AlreadySplitMarker"/> are the phrases the bench and
/// <c>shared/command-models.json</c> recognise an order by. They stay in code, before the editable text,
/// so no override can make a switch that worked read as one that did not.</para>
/// <para><b>Placeholders</b> — <c>{scope}</c>, <c>{strongest}</c>, <c>{implementation}</c>,
/// <c>{numbers}</c>, <c>{verdict}</c> — are the computed parts, filled by <see cref="GateCommands"/>. An
/// override that leaves one out simply does not say it: the order is the operator's words.</para>
/// </remarks>
public sealed record CommandTexts(IReadOnlyDictionary<string, string> Overrides)
{
    /// <summary>
    /// What every command text's id starts with, shipped or custom — a namespace in <c>&lt;dataDir&gt;/prompts/</c>
    /// that a role prompt may not enter, since both kinds of text live in that one folder.
    /// </summary>
    public const string Prefix = "command-";

    public const string Preamble = "command-preamble";
    public const string Autonomy = "command-autonomy";
    public const string Model = "command-model";
    public const string SplitNone = "command-split-none";
    public const string SplitSmall = "command-split-small";
    public const string SplitMedium = "command-split-medium";
    public const string SplitLarge = "command-split-large";
    public const string SplitHuge = "command-split-huge";
    public const string SplitMeasured = "command-split-measured";
    public const string CadenceEpic = "command-cadence-epic";
    public const string CadenceTask = "command-cadence-task";
    public const string CadenceSingle = "command-cadence-single";
    public const string AnotherCodeRound = "command-another-code-round";
    public const string AlreadySplitEpic = "command-already-split-epic";
    public const string AlreadySplitTask = "command-already-split-task";

    /// <summary>Every text this build ships — THE list; a test holds it and <c>shared/commands/</c> equal.</summary>
    public static readonly IReadOnlyList<string> ShippedIds =
    [
        Preamble, Autonomy, Model, SplitNone, SplitSmall, SplitMedium, SplitLarge, SplitHuge, SplitMeasured,
        CadenceEpic, CadenceTask, CadenceSingle, AnotherCodeRound, AlreadySplitEpic, AlreadySplitTask,
    ];

    /// <summary>What every release said before a person could change a word of it.</summary>
    public static CommandTexts Shipped { get; } = new(new Dictionary<string, string>());

    /// <summary>The text of one order part: a person's override when it says something, else the shipped one.</summary>
    /// <remarks>Only the END is trimmed — the file's newline. The start is kept as written: three of the
    /// texts continue the marker before them (<c>…already under way, so do NOT…</c>), and an override
    /// that begins with a space or a dash means it.</remarks>
    public string Text(string id) =>
        Overrides.TryGetValue(id, out var written) && !string.IsNullOrWhiteSpace(written)
            ? written.TrimEnd()
            : ShippedText(id);

    /// <summary>What a person wrote for this id, or empty — for a CUSTOM command, which ships no text to fall back to.</summary>
    public string Written(string id) =>
        Overrides.TryGetValue(id, out var written) && !string.IsNullOrWhiteSpace(written) ? written.TrimEnd() : string.Empty;

    /// <summary>The text compiled into this build, without the file's trailing newline.</summary>
    public static string ShippedText(string id) => Loaded.Value[id];

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Loaded = new(
        () => ShippedIds.ToDictionary(id => id, Embedded, StringComparer.Ordinal));

    /// <summary>One shipped text, or a refusal naming it — a build missing one is a broken build.</summary>
    private static string Embedded(string id)
    {
        var name = $"CoaiMcp.Core.commands.{id}.md";
        using var stream = typeof(CommandTexts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"the command text '{name}' is not embedded in this build — check shared/commands/ and the "
                + "EmbeddedResource item in CoaiMcp.Core.csproj");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().TrimEnd('\r', '\n');
    }
}
