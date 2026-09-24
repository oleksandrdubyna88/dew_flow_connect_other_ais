using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Commands;

namespace CoaiMcp.Server;

/// <summary>
/// <c>COAI_COMMANDS</c> as read: the commands a person added, and what could not be read (issue #467).
/// </summary>
/// <param name="Commands">The rows that could be used, in the order they were listed.</param>
/// <param name="Complaints">One sentence per value or row that could not be used.</param>
/// <remarks>
/// <para>The shape of <see cref="CommandModelsSetting"/>: an unreadable value is no custom commands and one
/// sentence on the panel, never a refused round — a custom command changes the words the caller is handed,
/// not what the gate decides.</para>
/// <para><b>The id is a FILE NAME</b> — <c>&lt;dataDir&gt;/prompts/command-&lt;id&gt;.md</c> — so it is held to
/// the slug the role prompts are held to, and the <c>command-</c> prefix is added here, never by the person:
/// a custom command can then never shadow a role's prompt, and a shipped command's name is refused rather
/// than silently becoming a second text for it.</para>
/// </remarks>
public sealed partial record CommandsSetting(IReadOnlyList<CustomCommand> Commands, IReadOnlyList<string> Complaints)
{
    /// <summary>The environment key this is read from.</summary>
    public const string Key = "COAI_COMMANDS";

    /// <summary>What every custom command's id starts with — the shipped ones' prefix too, and a namespace
    /// the role prompts are kept out of.</summary>
    public const string Prefix = CommandTexts.Prefix;

    /// <summary>The names a stage may be written as — names only: a number is not a stage.</summary>
    private static readonly Dictionary<string, CommandStage> Stages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["plan"] = CommandStage.Plan,
        ["code"] = CommandStage.Code,
        ["any"] = CommandStage.Any,
    };

    private static readonly CommandsSetting Absent = new([], []);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex Slug();

    public static CommandsSetting Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Absent;
        }

        try
        {
            // A JSON `null` is a readable value that is not a list: said, not thrown into the catch.
            return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ListCustomCommandDto) is { } rows
                ? Rows(rows)
                : Unreadable("it is the JSON value null rather than a list of commands");
        }
        catch (JsonException e)
        {
            return Unreadable(e.Message.Split(" LineNumber:")[0].Trim());
        }
    }

    private static CommandsSetting Unreadable(string why) => Absent with
    {
        Complaints = [$"{Key} could not be read ({why}) — no custom command is given until it is fixed"],
    };

    private static CommandsSetting Rows(List<CustomCommandDto?> rows)
    {
        var commands = new List<CustomCommand>();
        var complaints = new List<string>();
        foreach (var row in rows)
        {
            var why = WhyNot(row, commands);
            if (why.Length > 0)
            {
                complaints.Add($"{Key}: {why}");
            }
            else
            {
                commands.Add(CommandOf(row!));
            }
        }

        return new CommandsSetting(commands, complaints);
    }

    /// <summary>Why this row cannot be used, or empty when it can.</summary>
    private static string WhyNot(CustomCommandDto? row, IReadOnlyList<CustomCommand> kept)
    {
        var id = IdOf(row);
        if (id.Length == 0)
        {
            return "a row has no id";
        }
        var named = WhyNotNamed(id, kept);

        return named.Length > 0 ? named : WhyNotStaged(id, row!.Stage);
    }

    private static string IdOf(CustomCommandDto? row) => row?.Id?.Trim() ?? string.Empty;

    private static string WhyNotNamed(string id, IReadOnlyList<CustomCommand> kept)
    {
        if (!Slug().IsMatch(id))
        {
            return $"'{id}' is not a command name — lower-case letters, digits and hyphens";
        }
        if (CommandTexts.ShippedIds.Contains(Prefix + id, StringComparer.Ordinal))
        {
            return $"'{id}' is the name of a shipped command — override its text instead";
        }

        return kept.Any(command => command.Id == Prefix + id) ? $"'{id}' is listed twice — the first is kept" : string.Empty;
    }

    private static string WhyNotStaged(string id, string? stage) =>
        TryStage(stage, out _) ? string.Empty : $"'{id}' has the stage '{stage}' — plan, code or any";

    /// <summary>No stage is every round; otherwise one of the three names, in any case.</summary>
    /// <remarks>A table, not <c>Enum.TryParse</c>, which reads <c>"1"</c> as <c>Plan</c> — a command given
    /// in rounds the person never named. (codex and our own reviewer, the code round.)</remarks>
    private static bool TryStage(string? text, out CommandStage stage)
    {
        stage = CommandStage.Any;

        return string.IsNullOrWhiteSpace(text) || Stages.TryGetValue(text.Trim(), out stage);
    }

    /// <summary>A usable row: off unless switched on — as a role a person adds is — and titled by its id.</summary>
    private static CustomCommand CommandOf(CustomCommandDto row)
    {
        var id = IdOf(row);
        TryStage(row.Stage, out var stage);

        return new CustomCommand(Prefix + id, TitleOf(row.Title, id), row.Enabled ?? false, stage);
    }

    private static string TitleOf(string? title, string id) => string.IsNullOrWhiteSpace(title) ? id : title.Trim();
}
