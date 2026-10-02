using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// One base prompt a question row may be paired with: what it is called, the capability it needs, and
/// the shipped text (PLAN_question_consultant.md, §0 item 3 and A3).
/// </summary>
/// <param name="Id">The override file's name under <c>prompts/</c> — the role-prompt id rule.</param>
/// <param name="Title">What the settings row and the log call it.</param>
/// <param name="Capability">What the prompt NEEDS of its runtime — exactly one.</param>
/// <param name="Text">The shipped (or configured) instruction; a person's edit lives beside it as an override.</param>
/// <param name="Shipped">Whether it came with the binary — a shipped prompt has a default to restore to.</param>
public sealed record QuestionPromptDefinition(string Id, string Title, Capability Capability, string Text, bool Shipped);

/// <summary>What <c>COAI_QCONSULT_PROMPTS</c> turned out to be: the prompts a person added, and the complaints.</summary>
/// <param name="Unreadable">The whole value could not be parsed — no custom prompt exists, and the complaint says why.</param>
public sealed record QuestionPromptsSetting(
    IReadOnlyList<QuestionPromptDefinition> Prompts,
    IReadOnlyList<string> Complaints,
    bool Unreadable = false);

/// <summary>The file's shape, for the source-generated deserialiser.</summary>
internal sealed record QuestionPromptSeed(IReadOnlyList<string>? Why, IReadOnlyList<QuestionPromptSeedRow?>? Prompts);

internal sealed record QuestionPromptSeedRow(string? Id, string? Title, string? Capability, string? Text);

/// <summary>
/// The base prompts a row may name: the three shipped ones, embedded from <c>shared/question-prompts.json</c>,
/// plus whatever a person added through <c>COAI_QCONSULT_PROMPTS</c>.
/// </summary>
/// <remarks>
/// <para>Embedded for the reason <c>RuntimeCapabilities</c> and <c>RoleCatalog</c> give: a published
/// Native-AOT binary runs where no <c>shared/</c> directory exists, and a catalog that could go missing
/// is a row with no prompt. The extension generates its copy from the same file (S4).</para>
/// <para>A custom prompt is refused — by name, with its cure — when its id is not one an override file
/// can be named by, when it collides with a shipped id (the shipped text is edited through an override,
/// never redefined), when its capability is not one of the three, or when it has no text. A list that
/// cannot be parsed at all is NO custom prompt, said so; the shipped three are never in doubt.</para>
/// </remarks>
public sealed partial record QuestionPromptSet(IReadOnlyList<QuestionPromptDefinition> Prompts)
{
    internal const string SeedResource = "CoaiMcp.Core.question-prompts.json";

    /// <summary>The shipped three, read once from this assembly's manifest resource.</summary>
    public static QuestionPromptSet Shipped { get; } = LoadShipped();

    /// <summary>The shipped three with a person's prompts after them; a custom id never shadows a shipped one.</summary>
    public static QuestionPromptSet With(IReadOnlyList<QuestionPromptDefinition> custom) =>
        new([.. Shipped.Prompts, .. custom.Where(c => Shipped.Find(c.Id) is null)]);

    /// <summary>The prompt an id names — exactly, without case — or null.</summary>
    public QuestionPromptDefinition? Find(string id) =>
        Prompts.FirstOrDefault(p => string.Equals(p.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The ids a refusal spells.</summary>
    public string Spelled => string.Join(", ", Prompts.Select(p => p.Id));

    /// <summary>The prompts a person added, each checked, or why one of them is not there.</summary>
    public static QuestionPromptsSetting ParseCustom(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new QuestionPromptsSetting([], []);
        }

        try
        {
            var rows = JsonSerializer.Deserialize(json, CoreJsonContext.Default.ListQuestionPromptSeedRow)
                ?? throw new JsonException("it is the JSON value null rather than a list of prompts");

            return Checked(rows);
        }
        catch (JsonException e)
        {
            return new QuestionPromptsSetting(
                [],
                [$"COAI_QCONSULT_PROMPTS could not be read ({e.Message.Split(" LineNumber:")[0].Trim()}) — no custom prompt is offered until it is fixed; the shipped three still are"],
                Unreadable: true);
        }
    }

    private static QuestionPromptsSetting Checked(IReadOnlyList<QuestionPromptSeedRow?> rows)
    {
        var prompts = new List<QuestionPromptDefinition>();
        var complaints = new List<string>();
        foreach (var row in rows.Where(r => r is not null))
        {
            var (prompt, complaint) = One(row!, prompts);
            if (prompt is not null)
            {
                prompts.Add(prompt);
            }
            else
            {
                complaints.Add(complaint);
            }
        }

        return new QuestionPromptsSetting(prompts, complaints);
    }

    /// <summary>One custom row: a definition, or the sentence refusing it. Each check names the cure.</summary>
    private static (QuestionPromptDefinition? Prompt, string Complaint) One(QuestionPromptSeedRow row, IReadOnlyList<QuestionPromptDefinition> accepted)
    {
        var id = row.Id?.Trim() ?? string.Empty;
        if (!WellFormedId().IsMatch(id))
        {
            return (null, $"COAI_QCONSULT_PROMPTS: the prompt id '{id}' is not one an override file can be named by — lower-case letters, digits and dashes, starting with a letter or digit");
        }

        if (Shipped.Find(id) is not null || accepted.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return (null, $"COAI_QCONSULT_PROMPTS: the prompt id '{id}' is already taken — a shipped prompt is edited through its override, never redefined, and one id is one prompt");
        }

        return Body(row, id);
    }

    private static (QuestionPromptDefinition? Prompt, string Complaint) Body(QuestionPromptSeedRow row, string id)
    {
        if (!Capabilities.TryParse(row.Capability ?? string.Empty, out var capability))
        {
            return (null, $"COAI_QCONSULT_PROMPTS: the prompt '{id}' declares the capability '{row.Capability}', which is not one of: {Capabilities.AllSpelled}");
        }

        var text = row.Text?.Trim() ?? string.Empty;

        return text.Length == 0
            ? (null, $"COAI_QCONSULT_PROMPTS: the prompt '{id}' has no text — a row paired with it would be launched with nothing to say")
            : (new QuestionPromptDefinition(id, Title(row, id), capability, text, Shipped: false), string.Empty);
    }

    private static string Title(QuestionPromptSeedRow row, string id) =>
        row.Title?.Trim() is { Length: > 0 } title ? title : id;

    private static QuestionPromptSet LoadShipped()
    {
        using var stream = typeof(QuestionPromptSet).Assembly.GetManifestResourceStream(SeedResource)
            ?? throw Broken("it is not embedded in this build — check the EmbeddedResource item in CoaiMcp.Core.csproj");
        var seed = JsonSerializer.Deserialize(stream, CoreJsonContext.Default.QuestionPromptSeed)
            ?? throw Broken("it parsed to nothing");

        return FromSeed(seed);
    }

    /// <summary>The file as a catalog, refused whole if a shipped row is unusable — a broken BUILD, never a configuration.</summary>
    internal static QuestionPromptSet FromSeed(QuestionPromptSeed seed)
    {
        var rows = (seed.Prompts ?? []).Select(ShippedRow).ToList();
        if (rows.Count == 0)
        {
            throw Broken("it ships no prompt at all");
        }

        return new QuestionPromptSet(rows);
    }

    private static QuestionPromptDefinition ShippedRow(QuestionPromptSeedRow? row)
    {
        if (row is null || !WellFormedId().IsMatch(row.Id ?? string.Empty))
        {
            throw Broken($"the shipped prompt '{row?.Id}' has an id an override file cannot be named by");
        }

        if (!Capabilities.TryParse(row.Capability ?? string.Empty, out var capability))
        {
            throw Broken($"the shipped prompt '{row.Id}' declares the capability '{row.Capability}', outside {Capabilities.AllSpelled}");
        }

        return string.IsNullOrWhiteSpace(row.Text) || string.IsNullOrWhiteSpace(row.Title)
            ? throw Broken($"the shipped prompt '{row.Id}' has no text or no title")
            : new QuestionPromptDefinition(row.Id!, row.Title!.Trim(), capability, row.Text!.Trim(), Shipped: true);
    }

    private static InvalidOperationException Broken(string what) =>
        new($"the question prompt catalog ('{SeedResource}') is not usable: {what}. "
            + "It is embedded in this binary, so this is a broken build rather than anything a person configured.");

    /// <summary>The role-prompt id rule (<c>RoleComposition</c>): the id becomes an override file's name.</summary>
    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]*\z")]
    private static partial Regex WellFormedId();
}
