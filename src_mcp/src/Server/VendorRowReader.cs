using System.Text.Json;

namespace CoaiMcp.Server;

/// <summary>A row of `COAI_VENDORS` this server left out: who it was, and the sentence that says why.</summary>
/// <param name="Name">The row's id as the parser would make it (trimmed, lower-case), else its place: <c>row 2 of 3</c>.</param>
internal sealed record VendorRefusal(string Name, string Sentence);

/// <summary>`COAI_VENDORS` read row by row: the rows that read, the rows that did not, and why the text itself was refused.</summary>
/// <param name="Read">The rows whose members all read, in the order written. A row may still carry no usable id.</param>
/// <param name="Refused">The rows that did not read — a wrong-typed member, or a row that is null or not an object.</param>
/// <param name="Complaint">Empty when the text is a JSON list; else why it is not one (the parser's words, with its position).</param>
internal sealed record VendorRows(
    IReadOnlyList<VendorDto> Read,
    IReadOnlyList<VendorRefusal> Refused,
    string Complaint = "")
{
    public bool IsList => Complaint.Length == 0;
}

/// <summary>
/// Reads `COAI_VENDORS` one row at a time, so one bad member costs that row and nothing else
/// (todo/PLAN_one_bad_field_keeps_the_rows.md).
/// </summary>
/// <remarks>
/// <para>The whole list read in one call made a single wrong-typed member (<c>"timeoutMinutes":"30"</c>) throw, and the
/// caller ran the shipped pair in place of every row the person wrote. A row that does not read now fails CLOSED — it does
/// not review — and is named, through the same <see cref="UnrecognisedSetting"/> path every other refused setting uses.</para>
/// <para>It reads with the SAME <c>VendorDto</c> and the same generated type info as before; it maps nothing. The one
/// mapping to <see cref="ProviderSettings"/> stays in <c>PanelSettings.ParseVendors</c>.</para>
/// </remarks>
internal static class VendorRowReader
{
    internal const string Key = "COAI_VENDORS";

    private const string NoMember = "one of its members";

    /// <summary>Every row of <paramref name="json"/> that reads, and a refusal for each one that does not.</summary>
    internal static VendorRows Read(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);

            return parsed.RootElement.ValueKind == JsonValueKind.Array
                ? RowsOf(parsed.RootElement)
                : NotAList($"it is {Describe(parsed.RootElement.ValueKind)}, not a list of rows.");
        }
        catch (JsonException e)
        {
            return NotAList(PanelSettings.Detail(e));
        }
    }

    /// <summary>
    /// The member a <see cref="JsonException.Path"/> names, as the person wrote it: <c>timeoutMinutes</c>, <c>price.in</c>,
    /// or the bracket form <c>['odd name']</c> the parser uses for a name it cannot write with a dot.
    /// </summary>
    /// <remarks>The path is nullable and may be a bare <c>$</c>; neither names a member.</remarks>
    internal static string MemberOf(string? path) => path switch
    {
        null => NoMember,
        _ when string.IsNullOrWhiteSpace(path) || path == "$" => NoMember,
        _ when path.StartsWith("$.", StringComparison.Ordinal) => path[2..],
        _ when path.StartsWith('$') => path[1..],
        _ => path,
    };

    private static VendorRows RowsOf(JsonElement list)
    {
        var total = list.GetArrayLength();
        List<VendorDto> read = [];
        List<VendorRefusal> refused = [];
        foreach (var (row, index) in list.EnumerateArray().Select((row, index) => (row, index)))
        {
            switch (TryRow(row, $"row {index + 1} of {total}"))
            {
                case Outcome.Read one:
                    read.Add(one.Row);
                    break;
                case Outcome.Refused no:
                    refused.Add(no.Why);
                    break;
            }
        }

        return new VendorRows(read, refused);
    }

    private static Outcome TryRow(JsonElement row, string place)
    {
        if (row.ValueKind != JsonValueKind.Object)
        {
            return Refuse(row, place, $"it is {Describe(row.ValueKind)}, not a row.", "Write each reviewer as an object with an \"id\"");
        }

        try
        {
            return row.Deserialize(SettingsJsonContext.Default.VendorDto) is { } dto
                ? new Outcome.Read(dto)
                : Refuse(row, place, "it is null, not a row.", "Write each reviewer as an object with an \"id\"");
        }
        catch (JsonException e)
        {
            return Refuse(
                row, place, $"its member {MemberOf(e.Path)} has a value of the wrong JSON type.", "Write a number or true/false without quotes");
        }
    }

    private static Outcome.Refused Refuse(JsonElement row, string place, string reason, string cure)
    {
        var id = IdOf(row);
        var subject = id.Length > 0 ? $"the row \"{id}\" ({place})" : place;

        return new Outcome.Refused(new VendorRefusal(
            id.Length > 0 ? id : place,
            $"{Key}: {subject} is left out of every round — {reason} The other rows still review. "
          + $"{cure}, or save the reviewers in the panel again, which rewrites the list."));
    }

    /// <summary>
    /// The row's <c>id</c>, from the RAW element — when deserialization threw there is no DTO to ask. Compared the way the
    /// context reads names (ignoring case), taken only when it is a non-blank string, and never through <c>GetProperty</c>,
    /// which throws for a missing member.
    /// </summary>
    private static string IdOf(JsonElement row) => row.ValueKind != JsonValueKind.Object
        ? string.Empty
        : row.EnumerateObject()
            .Where(member => member.Name.Equals("id", StringComparison.OrdinalIgnoreCase) && member.Value.ValueKind == JsonValueKind.String)
            .Select(member => (member.Value.GetString() ?? string.Empty).Trim().ToLowerInvariant())
            .FirstOrDefault(id => id.Length > 0) ?? string.Empty;

    private static VendorRows NotAList(string complaint) => new([], [], complaint);

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "null",
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "a list",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        _ => "true or false",
    };

    /// <summary>What one row came to: a DTO, or the reason it was left out.</summary>
    private abstract record Outcome
    {
        private Outcome()
        {
        }

        internal sealed record Read(VendorDto Row) : Outcome;

        internal sealed record Refused(VendorRefusal Why) : Outcome;
    }
}
