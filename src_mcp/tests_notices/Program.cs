// The server half of the parity check, as a process a node script can drive. Verbs:
//
//   safe-text <limit>   stdin: a JSON array of strings
//                       stdout: a JSON array of `Redaction.SafeText(s, limit)`
//
//   serialise           stdin: a JSON array of record objects (the NotificationRecord shape)
//                       stdout: a JSON array of `ServerNoticeLine.Of(record)`, newline included
//
//   codes               stdout: a JSON array of every ServerNoticeCodes constant
//
//   append <path> <writer> <count>
//                       appends <count> records of the measurement harness's own shape — the
//                       `record()` of scripts/measure-append.mjs, field for field — each through
//                       `JsonlLedger.AppendLine`, the product's append. Exit 74 (EX_IOERR) naming
//                       the record the moment one could not be written, so the harness sees a
//                       process failure rather than a shortfall it has to infer.
//
// THE APPEND VERB MEASURES THE SYSTEM CALL, NOT THE SERIALISER. The harness checks every line's
// `pad.length` against the length it was written with, so two halves of two records cannot pass
// merely by parsing — and the notice serialiser caps `detail` at 4096, so a 60 KB notice would read
// as torn on every run. The records are therefore the harness's own shape, and the atomicity claim
// the measurement supports covers `JsonlLedger.AppendLine` — the FileStream flags, the single write,
// the torn-tail repair inside it — and says nothing about `ServerNoticeLine`. Parity says that.
//
// AND IT IS THE PRODUCT'S CALL, NOT A LOOKALIKE. This repository measured `appendFileSync` once and
// cited the result for `appendFile`; the correction is in the harness's own docstring — the same
// argument about O_APPEND is not the same measurement. A FileStream here with the same arguments
// would be the same mistake in the other language.
//
// WHY THIS EXISTS. `server-notices.jsonl` is written by C# and read by TypeScript, and both halves
// redact before anything reaches disk. Two suites that each compare themselves against the same
// fixture can both be wrong in the same direction; the only honest check runs BOTH implementations
// over the same inputs and compares the bytes. That needs the real `Redaction` and the real
// `ServerNoticeLine` in a process node can start, which is what this is.
//
// WHY A SEPARATE PROJECT rather than a mode of `coai-mcp`. A test-only verb in the shipped binary is
// a surface a person can reach and nobody maintains, and `PROJECT.md` keeps the sanctioned one-shot
// modes to a list. This is the `FakeCli` pattern: built by the solution, referenced by a script,
// never published.
//
// FRAMING. A serialised line ENDS in a newline, so line-delimited output could not carry it back
// unambiguously. Everything crosses as one JSON array in and one JSON array out.
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.ServiceDefaults;

// UTF-8 both ways, for the reason `FakeCli` records: without it Windows encodes stdout in the
// console code page and every Cyrillic character leaves as `?` — which is exactly the class of
// difference this tool exists to detect, so it must not be introduced by the tool itself.
Console.OutputEncoding = new UTF8Encoding(false);
Console.InputEncoding = new UTF8Encoding(false);

if (args.Length == 0)
{
    await Console.Error.WriteLineAsync("usage: NoticeTool <safe-text <limit> | serialise | codes | append <path> <writer> <count>>");
    return 64; // EX_USAGE
}

try
{
    switch (args[0])
    {
        case "safe-text":
            if (args.Length < 2 || !int.TryParse(args[1], out var limit))
            {
                await Console.Error.WriteLineAsync("safe-text needs a limit, a whole number");
                return 64;
            }

            Write([.. Read<string>().Select(one => Redaction.SafeText(one, limit))]);
            return 0;

        case "serialise":
            Write([.. Read<JsonElement>().Select(one => ServerNoticeLine.Of(NoticeFrom(one)))]);
            return 0;

        case "codes":
            Write([.. ServerNoticeCodes.All]);
            return 0;

        case "append":
            // NON-NEGATIVE, because `append ledger.jsonl 0 -1` otherwise parses, skips the loop and
            // exits 0 — a harness or a person then reads "success" for a run that wrote nothing.
            if (args.Length < 4
                || !int.TryParse(args[2], out var writer) || writer < 0
                || !int.TryParse(args[3], out var count) || count < 0)
            {
                await Console.Error.WriteLineAsync(
                    "append needs <path> <writer> <count>, the last two whole numbers and neither negative");
                return 64;
            }

            return await AppendMany(args[1], writer, count);

        default:
            await Console.Error.WriteLineAsync($"NoticeTool has no verb '{args[0]}'");
            return 64;
    }
}
catch (Exception e)
{
    // A stand-in that dies unhandled destroys the evidence it exists to produce: the script driving
    // it would see an empty stdout and report "the two halves disagree" about a crash.
    await Console.Error.WriteLineAsync($"NoticeTool failed: {e.GetType().Name}: {e.Message}");
    return 70; // EX_SOFTWARE
}

/// <summary>The measurement's writer: <paramref name="count"/> records of the harness's shape, each through the product's append.</summary>
static async Task<int> AppendMany(string path, int writer, int count)
{
    for (var index = 0; index < count; index++)
    {
        if (!JsonlLedger.AppendLine(path, HarnessLine(writer, index)))
        {
            await Console.Error.WriteLineAsync(
                $"NoticeTool append: record {writer}:{index} could not be written to {path}");

            return 74; // EX_IOERR
        }
    }

    return 0;
}

/// <summary>
/// <c>{"utc":…,"writer":N,"index":N,"pad":"aaa…"}</c> and its newline — the harness's <c>record()</c>,
/// field for field, at the same paddings.
/// </summary>
/// <remarks>
/// Spelled by hand rather than through a serializer: every value is a number, an ISO instant or a
/// run of one ASCII letter, so the text is exactly what <c>JSON.stringify</c> writes and there is
/// no encoder to disagree with. The paddings are the harness's own — a pipe buffer, a page, a
/// filesystem block and past them — and must stay so, because the harness checks each line's
/// <c>pad.length</c> against <c>PADDINGS[index % 5]</c> to catch two halves of two records.
/// </remarks>
static string HarnessLine(int writer, int index)
{
    int[] paddings = [0, 200, 4000, 8200, 60_000];
    var pad = new string((char)('a' + writer % 26), paddings[index % paddings.Length]);
    var utc = ServerNotice.Iso(DateTimeOffset.UtcNow);

    return $"{{\"utc\":\"{utc}\",\"writer\":{writer},\"index\":{index},\"pad\":\"{pad}\"}}\n";
}

static List<T> Read<T>()
{
    var text = Console.In.ReadToEnd();

    return JsonSerializer.Deserialize<List<T>>(text)
        ?? throw new InvalidOperationException("stdin parsed to nothing — a JSON array was expected");
}

// CODE UNITS, not strings, and the harness is what taught me to. The first version wrote the answers
// as JSON strings through `UnsafeRelaxedJsonEscaping` — and the very first parity run reported a
// difference at code unit 999 of a 1000-character cut: JavaScript keeps the lone HIGH SURROGATE that
// `slice` leaves behind when it cuts through an emoji (0xD83D), and the encoder had replaced it with
// U+FFFD. The PORT was right; the transport was lying about it.
//
// So nothing in this path encodes text. Each answer crosses as an array of its UTF-16 code units,
// which is lossless for a lone surrogate, a NUL, U+2028 and every other character a terminal, an
// encoder or a diff would render identically to something else.
static void Write(IReadOnlyList<string> answers) =>
    Console.Out.Write(JsonSerializer.Serialize(
        answers.Select(one => one.Select(c => (int)c).ToArray()).ToArray()));

/// <summary>A field this build has no name for, as the value `more` may carry.</summary>
static object Stranger(string name, JsonElement value) =>
    value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.TryGetInt64(out var whole) ? whole : value.GetDouble(),
        _ => throw new InvalidOperationException(
            $"`more` may carry strings and finite numbers only; '{name}' is {value.ValueKind}"),
    };

/// <summary>One record as the script sent it, refusing a field this build cannot place.</summary>
static ServerNotice NoticeFrom(JsonElement row)
{
    string? Text(string name) =>
        row.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;

    int? Number(string name) =>
        row.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.Number
            ? found.GetInt32()
            : null;

    var known = new HashSet<string>(ServerNoticeLine.NamedFields, StringComparer.Ordinal);
    var more = row.EnumerateObject()
        .Where(p => !known.Contains(p.Name))
        .ToDictionary(p => p.Name, p => Stranger(p.Name, p.Value), StringComparer.Ordinal);

    return new ServerNotice
    {
        Utc = Text("utc") ?? throw new InvalidOperationException("a record needs a `utc`"),
        Class = Text("class") ?? throw new InvalidOperationException("a record needs a `class`"),
        Source = Text("source") ?? throw new InvalidOperationException("a record needs a `source`"),
        Code = Text("code") ?? throw new InvalidOperationException("a record needs a `code`"),
        Subject = Text("subject"),
        Title = Text("title"),
        Detail = Text("detail"),
        Cure = Text("cure"),
        Action = Text("action"),
        Offered = Text("offered"),
        Answer = Text("answer"),
        Run = Text("run"),
        Repo = Text("repo"),
        Branch = Text("branch"),
        Session = Text("session"),
        Provider = Text("provider"),
        Role = Text("role"),
        Pid = Number("pid"),
        Seq = Number("seq"),
        Bound = Number("bound"),
        More = more.Count == 0 ? null : more,
    };
}
