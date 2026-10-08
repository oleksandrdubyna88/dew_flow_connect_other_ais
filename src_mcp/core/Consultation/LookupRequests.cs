using CoaiMcp.Core.Feature;

namespace CoaiMcp.Core.Consultation;

/// <summary>One thing an antigravity consultant asked coai to look at — the line as written, and what it asked.</summary>
/// <param name="Line">The request line, trimmed — what a refusal or a result names.</param>
public abstract record LookupRequest(string Line)
{
    /// <summary><c>list &lt;folder&gt;</c>: one level of a folder.</summary>
    public sealed record List(string Line, string Path) : LookupRequest(Line);

    /// <summary><c>search "&lt;text&gt;" in &lt;folder&gt;</c>: a LITERAL, case-insensitive substring; an empty path is the single root.</summary>
    public sealed record Search(string Line, string Text, string Path) : LookupRequest(Line);
}

/// <summary>What one answer asked for.</summary>
/// <param name="Requests">The lookups to serve, at most <see cref="LookupBudget.RequestsPerTurn"/>.</param>
/// <param name="Refused">Every other line of a block, each with why — said back to the model, never silently dropped.</param>
/// <param name="Prose">The answer with its blocks taken out — the draft of a request turn, the answer of the last one.</param>
/// <param name="HadBlock">Whether the answer carried a block at all — the one thing that makes a turn a request turn.</param>
public sealed record LookupAsk(IReadOnlyList<LookupRequest> Requests, IReadOnlyList<string> Refused, string Prose, bool HadBlock);

/// <summary>
/// Reads the <c>coai-lookup</c> block an antigravity consultant writes when it needs coai to list a folder or search
/// for text (todo/PLAN_agy_searches_through_coai.md §3) — agy answers in prose, not a schema, so a fenced block it can
/// write anywhere in its answer.
/// </summary>
/// <remarks>
/// <para><b>Never a regex.</b> A search is a literal substring, so no text a model writes can make coai's own scan slow
/// or match more than it says.</para>
/// <para><b>An unclosed block is still a request</b>: a model cut off mid-block asked for something, and taking its text
/// as the answer would hand the person a half-written request as advice.</para>
/// <para>Pure: the serving — containment, caps, redaction — is the reader's job, not this one's.</para>
/// </remarks>
public static class LookupRequests
{
    /// <summary>The info string of the fence that opens a block: <c>```coai-lookup</c>.</summary>
    public const string Fence = "coai-lookup";

    private const string Closing = "```";

    public static LookupAsk Read(string answer) => Read(answer, LookupBudget.RequestsPerTurn);

    /// <param name="cap">The most requests one turn may ask for; the rest are refused by name.</param>
    public static LookupAsk Read(string answer, int cap)
    {
        var prose = new List<string>();
        var asked = new List<string>();
        var hadBlock = false;
        var inBlock = false;
        foreach (var line in answer.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            (inBlock, hadBlock) = Route(line, inBlock, hadBlock, prose, asked);
        }

        var (requests, refused) = Parse(asked, cap);

        return new LookupAsk(requests, refused, Tidy(prose), hadBlock);
    }

    /// <summary>One line of the answer: a fence that opens or closes a block, a line of a block, or prose.</summary>
    private static (bool InBlock, bool HadBlock) Route(string line, bool inBlock, bool hadBlock, List<string> prose, List<string> asked) =>
        inBlock ? InBlock(line.Trim(), hadBlock, asked) : OutOfBlock(line, hadBlock, prose, asked);

    /// <summary>
    /// A line of prose — or one that opens a block ANYWHERE in it: the text before the fence stays prose, anything after
    /// it on the same line is the block's first line. Live, agy glued the fence to the end of a sentence
    /// (research/RESULTS_agy_searches_through_coai.md, Windows run 2), and the row's answer became the block itself.
    /// </summary>
    private static (bool InBlock, bool HadBlock) OutOfBlock(string line, bool hadBlock, List<string> prose, List<string> asked)
    {
        var at = line.IndexOf(Closing + Fence, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            prose.Add(line);
            return (false, hadBlock);
        }

        if (line[..at].Trim().Length > 0)
        {
            prose.Add(line[..at]);
        }

        var after = line[(at + Closing.Length + Fence.Length)..].Trim();

        return after.Length > 0 ? InBlock(after, true, asked) : (true, true);
    }

    /// <summary>A line inside a block: a request line, a closing fence — or both, when the fence is glued to the line's end.</summary>
    private static (bool InBlock, bool HadBlock) InBlock(string trimmed, bool hadBlock, List<string> asked)
    {
        if (!trimmed.EndsWith(Closing, StringComparison.Ordinal))
        {
            asked.Add(trimmed);
            return (true, hadBlock);
        }

        asked.Add(trimmed[..^Closing.Length].Trim());

        return (false, hadBlock);
    }

    /// <summary>One line of a block, read: a request, or the sentence saying why the line is not one.</summary>
    private abstract record Parsed
    {
        private Parsed()
        {
        }

        public sealed record Asked(LookupRequest Request) : Parsed;

        public sealed record NotARequest(string Why) : Parsed;
    }

    private static (IReadOnlyList<LookupRequest> Requests, IReadOnlyList<string> Refused) Parse(List<string> lines, int cap)
    {
        var requests = new List<LookupRequest>();
        var refused = new List<string>();
        foreach (var line in lines.Where(l => l.Length > 0))
        {
            switch (One(line))
            {
                case Parsed.Asked asked when requests.Count < cap:
                    requests.Add(asked.Request);
                    break;
                case Parsed.Asked:
                    refused.Add($"{line} — more than {cap} requests in one turn; ask again next turn");
                    break;
                case Parsed.NotARequest no:
                    refused.Add($"{line} — {no.Why}");
                    break;
            }
        }

        return (requests, refused);
    }

    private static Parsed One(string line)
    {
        var (verb, rest) = Split(line);

        return verb.ToLowerInvariant() switch
        {
            "list" => new Parsed.Asked(new LookupRequest.List(line, rest.Length == 0 ? "." : Unquoted(rest))),
            "search" => SearchOf(line, rest),
            _ => new Parsed.NotARequest("not a lookup — a block holds only `list <folder>` and `search \"<text>\" in <folder>` lines"),
        };
    }

    private static Parsed SearchOf(string line, string rest)
    {
        var close = ClosingQuote(rest);

        return close <= 1
            ? new Parsed.NotARequest("quote the text you search for: search \"<text>\" in <folder>")
            : InFolder(line, rest[1..close], rest[(close + 1)..].Trim());
    }

    /// <summary>Where the quoted text ends — -1 when the rest does not open with a quote.</summary>
    private static int ClosingQuote(string rest) => rest.Length > 1 && rest[0] == '"' ? rest.IndexOf('"', 1) : -1;

    /// <summary>What may follow the quoted text: nothing (the single root), or <c>in &lt;folder&gt;</c>.</summary>
    private static Parsed InFolder(string line, string text, string after) =>
        after.Length == 0 ? new Parsed.Asked(new LookupRequest.Search(line, text, string.Empty))
        : after.StartsWith("in ", StringComparison.OrdinalIgnoreCase) ? new Parsed.Asked(new LookupRequest.Search(line, text, Unquoted(after[3..].Trim())))
        : new Parsed.NotARequest("after the quoted text only `in <folder>` may follow");

    private static (string Verb, string After) Split(string line)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? (line, string.Empty) : (line[..space], line[(space + 1)..].Trim());
    }

    private static string Unquoted(string path) =>
        path.Length > 1 && path[0] == '"' && path[^1] == '"' ? path[1..^1] : path;

    /// <summary>The prose with the blocks taken out: runs of blank lines folded to one, the ends trimmed.</summary>
    private static string Tidy(List<string> lines)
    {
        var kept = new List<string>();
        foreach (var line in lines)
        {
            if (line.Trim().Length > 0 || (kept.Count > 0 && kept[^1].Trim().Length > 0))
            {
                kept.Add(line.TrimEnd());
            }
        }

        return string.Join("\n", kept).Trim();
    }
}
