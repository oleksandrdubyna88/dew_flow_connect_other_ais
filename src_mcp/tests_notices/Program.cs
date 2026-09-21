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

// UTF-8 both ways, for the reason `FakeCli` records: without it Windows encodes stdout in the
// console code page and every Cyrillic character leaves as `?` — which is exactly the class of
// difference this tool exists to detect, so it must not be introduced by the tool itself.
Console.OutputEncoding = new UTF8Encoding(false);
Console.InputEncoding = new UTF8Encoding(false);

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: NoticeTool <safe-text <limit> | serialise | codes>");
    return 64; // EX_USAGE
}

try
{
    switch (args[0])
    {
        case "safe-text":
            if (args.Length < 2 || !int.TryParse(args[1], out var limit))
            {
                Console.Error.WriteLine("safe-text needs a limit, a whole number");
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

        default:
            Console.Error.WriteLine($"NoticeTool has no verb '{args[0]}'");
            return 64;
    }
}
catch (Exception e)
{
    // A stand-in that dies unhandled destroys the evidence it exists to produce: the script driving
    // it would see an empty stdout and report "the two halves disagree" about a crash.
    Console.Error.WriteLine($"NoticeTool failed: {e.GetType().Name}: {e.Message}");
    return 70; // EX_SOFTWARE
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
        .ToDictionary(p => p.Name, p => p.Value.ValueKind switch
        {
            JsonValueKind.String => (object)p.Value.GetString()!,
            JsonValueKind.Number => p.Value.TryGetInt64(out var whole) ? whole : p.Value.GetDouble(),
            _ => throw new InvalidOperationException(
                $"`more` may carry strings and finite numbers only; '{p.Name}' is {p.Value.ValueKind}"),
        }, StringComparer.Ordinal);

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
