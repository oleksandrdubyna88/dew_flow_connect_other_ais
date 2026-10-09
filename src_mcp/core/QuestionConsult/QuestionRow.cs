using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// One row of the question consultant: a model and exactly one base prompt, on or off
/// (PLAN_question_consultant.md, §0 item 3 and the accepted defaults).
/// </summary>
/// <param name="Id">The row's own id — what the record, the ledger and the reply name it by.</param>
/// <param name="Vendor">The vendor id: a definition's own, or a reviewer row's to borrow runtime and path from.</param>
/// <param name="Runtime">Which CLI answers; empty borrows the reviewer row named by <paramref name="Vendor"/>.</param>
/// <param name="Model">The model, or empty for the runtime's own default.</param>
/// <param name="BaseUrl">An endpoint, for an <c>api</c> or custom-codex row.</param>
/// <param name="ExecutablePath">Where the CLI is; empty looks it up on PATH.</param>
/// <param name="Key">The vault entry an <c>api</c> row's key is filed under, when not its vendor id (S3.6). A name, never a value.</param>
/// <param name="Prompt">The id of the ONE base prompt this row runs — the same prompt may sit on several rows.</param>
/// <param name="Enabled">Whether the row runs. At most <see cref="QuestionRows.MaxActive"/> rows may be on.</param>
/// <param name="Row">The catalog row this row refers to, as the extension wrote it — its options (effort, system prompt,
/// timeout, price) for the launch (research/PLAN_one_model_catalog.md, C2). Raw JSON, read where <c>ProviderSettings</c> is
/// known; empty when the row carries none.</param>
public sealed record QuestionRow(
    string Id,
    string Vendor,
    string Runtime,
    string Model,
    string BaseUrl,
    string ExecutablePath,
    string Key,
    string Prompt,
    bool Enabled,
    string Row = "");

/// <summary>What <c>COAI_QCONSULT_ROWS</c> turned out to be: the rows, the complaints, and whether it could be read at all.</summary>
/// <param name="Unreadable">The whole value could not be parsed — the tool refuses by name rather than running nobody.</param>
public sealed record QuestionRowsSetting(
    IReadOnlyList<QuestionRow> Rows,
    IReadOnlyList<string> Complaints,
    bool Unreadable = false)
{
    /// <summary>The rows that run: enabled, within the cap.</summary>
    public IReadOnlyList<QuestionRow> Active => [.. Rows.Where(r => r.Enabled)];
}

/// <summary>The wire shape of one row, every field nullable: an omitted field arrives null whatever a declaration says.</summary>
internal sealed record QuestionRowDto(
    string? Id = null,
    string? Vendor = null,
    string? Runtime = null,
    string? Model = null,
    string? BaseUrl = null,
    string? ExecutablePath = null,
    string? Key = null,
    string? Prompt = null,
    bool? Enabled = null,
    JsonElement? Row = null);

/// <summary>
/// The parser of <c>COAI_QCONSULT_ROWS</c>: a JSON array of rows, at most <see cref="MaxActive"/> of them on.
/// </summary>
/// <remarks>
/// <para>Every refusal is a sentence naming the row and the cure, and never half a list: a row with no
/// id, no vendor or no prompt is dropped and said; a second row under one id is dropped and said; the
/// seventh active row and every one after it is switched OFF and said — the cap is the operator's
/// accepted default, and a server that ran a seventh because the file carried one would be the panel's
/// limit undone by a hand edit.</para>
/// <para>The same prompt on several rows is ordinary (Sonnet and Opus both on "projects on this disk"),
/// so nothing here is unique but the id.</para>
/// </remarks>
public static partial class QuestionRows
{
    /// <summary>The operator's accepted default: six rows at once, at most.</summary>
    public const int MaxActive = 6;

    public static QuestionRowsSetting Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new QuestionRowsSetting([], []);
        }

        try
        {
            var rows = JsonSerializer.Deserialize(json, CoreJsonContext.Default.ListQuestionRowDto)
                ?? throw new JsonException("it is the JSON value null rather than a list of rows");

            return Checked(rows);
        }
        catch (JsonException e)
        {
            return new QuestionRowsSetting(
                [],
                [$"COAI_QCONSULT_ROWS could not be read ({e.Message.Split(" LineNumber:")[0].Trim()}) — the question consultant refuses until it is fixed"],
                Unreadable: true);
        }
    }

    private static QuestionRowsSetting Checked(IReadOnlyList<QuestionRowDto?> dtos)
    {
        var rows = new List<QuestionRow>();
        var complaints = new List<string>();
        foreach (var dto in dtos.Where(d => d is not null))
        {
            var (row, complaint) = One(dto!, rows);
            if (row is not null)
            {
                rows.Add(row);
            }
            else
            {
                complaints.Add(complaint);
            }
        }

        return new QuestionRowsSetting(Capped(rows, complaints), complaints);
    }

    /// <summary>One row: itself, or the sentence refusing it.</summary>
    private static (QuestionRow? Row, string Complaint) One(QuestionRowDto dto, IReadOnlyList<QuestionRow> accepted)
    {
        var id = dto.Id?.Trim() ?? string.Empty;
        if (!WellFormedId().IsMatch(id))
        {
            return (null, $"COAI_QCONSULT_ROWS: a row has the id '{id}', which is not one a record can be named by — letters, digits, dot, dash and underscore, up to 64");
        }

        if (accepted.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return (null, $"COAI_QCONSULT_ROWS: the row id '{id}' appears twice — one id is one row, and the second was dropped");
        }

        return Fields(dto, id);
    }

    private static (QuestionRow? Row, string Complaint) Fields(QuestionRowDto dto, string id)
    {
        var vendor = dto.Vendor?.Trim() ?? string.Empty;
        var prompt = dto.Prompt?.Trim() ?? string.Empty;
        if (vendor.Length == 0 || prompt.Length == 0)
        {
            return (null, $"COAI_QCONSULT_ROWS: the row '{id}' names {(vendor.Length == 0 ? "no vendor" : "no prompt")} — a row is a model and exactly one base prompt");
        }

        return (new QuestionRow(
            id,
            vendor,
            dto.Runtime?.Trim().ToLowerInvariant() ?? string.Empty,
            dto.Model?.Trim() ?? string.Empty,
            dto.BaseUrl?.Trim() ?? string.Empty,
            dto.ExecutablePath?.Trim() ?? string.Empty,
            dto.Key?.Trim().ToLowerInvariant() ?? string.Empty,
            prompt,
            Enabled: dto.Enabled != false,
            Row: RowText(dto.Row)), string.Empty);
    }

    /// <summary>The catalog row as it was written, for the resolver to read — empty when the row carries none.</summary>
    private static string RowText(JsonElement? row) =>
        row is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } written ? written.GetRawText() : string.Empty;

    /// <summary>The seventh active row and after are switched off, and the complaint names them.</summary>
    private static List<QuestionRow> Capped(List<QuestionRow> rows, List<string> complaints)
    {
        var active = 0;
        var capped = new List<QuestionRow>(rows.Count);
        foreach (var row in rows)
        {
            var on = row.Enabled && ++active <= MaxActive;
            if (row.Enabled && !on)
            {
                complaints.Add($"COAI_QCONSULT_ROWS: the row '{row.Id}' is the {Ordinal(active)} active row and at most {MaxActive} may be on — it was switched off; disable another row to run it");
            }

            capped.Add(row with { Enabled = on });
        }

        return capped;
    }

    private static string Ordinal(int n) => n switch
    {
        7 => "seventh",
        8 => "eighth",
        9 => "ninth",
        _ => $"{n}th",
    };

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z")]
    private static partial Regex WellFormedId();
}
