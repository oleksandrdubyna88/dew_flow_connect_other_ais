using System.Text.Json;
using CoaiMcp.Core.Commands;

namespace CoaiMcp.Server;

/// <summary>
/// <c>COAI_COMMAND_MODELS</c> as read: the caller kinds a person configured, and what could not be read.
/// </summary>
/// <param name="Map">Only the kinds that were configured, keyed lower-case and trimmed; the rest resolve
/// through <see cref="CommandModels.For"/>. Empty when absent or unreadable.</param>
/// <param name="Complaints">One sentence when the value could not be read, else none.</param>
/// <remarks>
/// <para>The shape of <c>ConsultantRouting.Parse</c>, minus its refusal. A consultant nobody chose is
/// a call to a vendor nobody picked, so an unreadable consultant setting refuses; this setting only
/// changes the WORDS of an order the caller is free to disagree with, so an unreadable one falls back
/// to the shipped pairs and says so on the panel — refusing a round over it would cost more than it
/// protects. (Issue #117's plan, constraint 5.)</para>
/// </remarks>
public sealed record CommandModelsSetting(
    IReadOnlyDictionary<string, ModelPair> Map,
    IReadOnlyList<string> Complaints)
{
    /// <summary>The environment key this is read from.</summary>
    public const string Key = "COAI_COMMAND_MODELS";

    private static readonly CommandModelsSetting Absent = new(new Dictionary<string, ModelPair>(), []);

    public static CommandModelsSetting Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Absent;
        }

        try
        {
            var rows = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.DictionaryStringCommandModelDto)
                ?? throw new JsonException("it is the JSON value null rather than an object of caller kinds");

            return new CommandModelsSetting(Rows(rows), []);
        }
        catch (JsonException e)
        {
            return Absent with
            {
                Complaints =
                [
                    $"{Key} could not be read ({e.Message.Split(" LineNumber:")[0].Trim()}) — "
                        + "the split order names the shipped models until it is fixed",
                ],
            };
        }
    }

    /// <summary>Every row that names something, trimmed; a kind this build does not know is KEPT.</summary>
    /// <remarks>
    /// Kept because a newer panel may know a caller kind this server does not, and dropping it would
    /// lose the person's choice on the day this server is updated. A null row is skipped: the DTO's
    /// nullables are the wire's shape and none of them survives past this method.
    /// </remarks>
    private static Dictionary<string, ModelPair> Rows(Dictionary<string, CommandModelDto?> rows) =>
        rows
            .Where(row => row.Value is not null && row.Key.Trim().Length > 0)
            .ToDictionary(
                row => row.Key.Trim().ToLowerInvariant(),
                row => new ModelPair(Trimmed(row.Value!.Strongest), Trimmed(row.Value.Implementation)),
                StringComparer.Ordinal);

    private static string Trimmed(string? value) => value?.Trim() ?? string.Empty;
}
