using System.Text.Json;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Server;
using CoaiMcp.Store;

namespace CoaiMcp.Collecting;

/// <summary>
/// `--pairs-decide --in &lt;decisions.json&gt;`: a batch of keeps AND comments, written together.
/// </summary>
/// <remarks>
/// <para><b>A mode of its own, not a wider `--pairs-keep`.</b> An older binary handed a comment in
/// `--pairs-keep`'s file deserialises past it and answers `{"decided": N}` — plausible, and the words
/// gone in silence. A mode the binary does not have exits 64, which is the one answer the panel can
/// act on: it says the server is too old for comments and keeps the draft.</para>
/// <para><b>So 64 means ONLY "never heard of this mode".</b> Everything wrong with a request is 65 —
/// no `--in`, an unreadable file, malformed JSON, no `items`, a keep that is not a decision, a
/// comment <see cref="CommentRule"/> refuses, a changed comment on a pair already sent — and a
/// database that will not open or be read is 74. A 64 from THIS binary would be read by the panel as
/// an old one, and a failed decision would look like a compatibility fallback.</para>
/// <para><b>A unit of its own rather than more of <c>Program.cs</c></b>, which was 1 878 lines
/// against the 800 the conventions allow; the conventions forbid a <c>partial</c> to duck that
/// ceiling, and this is the named unit they ask for instead. It borrows <c>Program</c>'s four
/// helpers — flags, the note on stderr, and the two that recognise an unreadable database — so the
/// answers are spelled the way every other mode spells them. (Plan round of 4.2, gemini.)</para>
/// <para><b>What is written is what the rule was run over.</b> Line endings are made LF and the
/// text trimmed FIRST, then checked: a CR the box never meant would otherwise be refused as a control
/// character, and a comment that is empty after trimming is no comment at all. A refusal names the
/// pair and the reason — a length or a code point — and never the text.</para>
/// </remarks>
internal static class PairsDecideMode
{
    private const string Mode = "--pairs-decide";

    internal static int Run(string[] args)
    {
        var asked = Asked(args, out var refusal);
        if (refusal.Length > 0)
        {
            Program.Note(refusal);
            return 65; // EX_DATAERR
        }

        var settings = PanelSettings.FromEnvironment(Environment.GetEnvironmentVariable);
        using var db = RoundsDb.Open(settings.DataDir, Serilog.Core.Logger.None);
        if (db is null)
        {
            Program.Note("the rounds database could not be opened; no decision can be written to it");
            return 74; // EX_IOERR
        }

        return Write(db, asked);
    }

    /// <summary>The write, and the two ways it can end other than in a count.</summary>
    private static int Write(RoundsDb db, IReadOnlyList<CommentedDecision> asked)
    {
        try
        {
            var (decided, refusal) = db.RecordDecide(asked);
            if (refusal.Length > 0)
            {
                Program.Note($"{Mode}: {refusal}");
                return 65; // EX_DATAERR
            }

            Console.Out.WriteLine(JsonSerializer.Serialize(
                new KeepAnswer(decided), ServerJsonContext.Default.KeepAnswer));

            return 0;
        }
        catch (Exception e) when (Program.Unreadable(e))
        {
            Program.Note(Program.WhyUnreadable(e));
            return 74; // EX_IOERR
        }
    }

    /// <summary>The decisions a file asked for, normalised, or why it is not a request.</summary>
    private static IReadOnlyList<CommentedDecision> Asked(string[] args, out string refusal)
    {
        // Whitespace as well as absent: `--in ""` reaches File.ReadAllText as an ArgumentException.
        if (!Program.Flags(args).TryGetValue("--in", out var input) || string.IsNullOrWhiteSpace(input))
        {
            refusal = $"{Mode} needs --in <decisions.json>";
            return [];
        }

        DecideRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(File.ReadAllText(input), ServerJsonContext.Default.DecideRequest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException or JsonException)
        {
            refusal = $"{Mode} could not read {input}: {e.Message}";
            return [];
        }

        return Wanted(request, out refusal);
    }

    /// <summary>What a parsed document asked for, once every keep and every comment is acceptable.</summary>
    /// <remarks>
    /// A document with no `items` is malformed rather than empty, for `--pairs-keep`'s reason: `{}`
    /// must not look like a successful batch of nothing. An explicit `[]` stays a legitimate no-op.
    /// </remarks>
    private static IReadOnlyList<CommentedDecision> Wanted(DecideRequest? request, out string refusal)
    {
        if (request?.Items is not { } items)
        {
            refusal = $"{Mode}: the document has no `items` list";
            return [];
        }

        var decisions = items.Select(one => new CommentedDecision(one.FindingId, one.Keep, Normal(one.Comment))).ToList();
        refusal = FirstRefusal(decisions);

        return refusal.Length == 0 ? decisions : [];
    }

    /// <summary>The first decision this will not write, said, or empty when there is none.</summary>
    private static string FirstRefusal(IReadOnlyList<CommentedDecision> decisions)
    {
        foreach (var decision in decisions)
        {
            var why = Refusal(decision);
            if (why.Length > 0)
            {
                return $"{Mode}: pair {decision.FindingId}: {why}";
            }
        }

        return string.Empty;
    }

    private static string Refusal(CommentedDecision decision) =>
        Keep.IsDecision(decision.Keep)
            ? CommentRule.Refuse(decision.Comment)
            : "every decision must be -1 (undecided), 0 (dropped) or 1 (kept)";

    /// <summary>LF line endings, no blank space at either end — what a person meant, not what the box sent.</summary>
    /// <remarks>Null-safe because the field came from a document: an explicit `"comment": null` is none.</remarks>
    private static string Normal(string? comment) =>
        (comment ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
}
