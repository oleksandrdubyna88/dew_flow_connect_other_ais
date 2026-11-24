using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoaiMcp.Server.Keys;

/// <summary>
/// The verbs of <see cref="KeysMode"/>, one method each: they read their arguments, call the pool and print the answer. The answer is ONE JSON line with <c>--json</c> and a few plain lines without.
/// </summary>
internal sealed partial class KeyVerbs(KeysRuntime runtime, KeyStoreAccess access, KeyArgs args)
{
    private readonly KeyAdmin _admin = new(access);

    [GeneratedRegex("^[a-z0-9:_./-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceName();

    private Func<string, string?> Layered => Roles.RolesMode.Layered(runtime.Settings);

    // -------------------------------------------------------------------------------------------------------------
    // list, add
    // -------------------------------------------------------------------------------------------------------------

    public int List()
    {
        var answer = _admin.List(Layered(KeysMode.PersonSignInSetting) == "1");
        if (answer.Value is not { } list)
        {
            return Refused(answer);
        }

        if (args.Json)
        {
            runtime.Output.WriteLine(KeysMode.Line(list, KeysAnswerJson.Default.KeyListAnswer));

            return 0;
        }

        runtime.Output.WriteLine($"{list.Rows.Count} key(s), {list.Usable} usable (list revision {list.AdminRevision}; person sign-in switch {(list.PersonSignInSwitch ? "on" : "off")})");
        foreach (var row in list.Rows)
        {
            runtime.Output.WriteLine($"{row.Id}  {row.Label}  {row.State}{(row.Problem.Length > 0 ? " (" + row.Problem + ")" : string.Empty)}{(row.Usable ? string.Empty : "  not usable now")}  launches {row.Launches}");
        }

        return 0;
    }

    public int Add()
    {
        if (!args.Has("--label"))
        {
            runtime.Note("--keys add needs --label <name> (the key itself is ONE line on stdin)");

            return KeyExit.Data;
        }

        if (args.Has("--account") && !KeySourceReader.IsAccount(args.Get("--account")))
        {
            runtime.Note("--keys add: the account must be an email-shaped text without spaces");

            return KeyExit.Data;
        }

        var text = ReadKeyLine();
        if (text.Value is not { } token)
        {
            return Refused(text);
        }

        var before = _admin.List(personSignInSwitch: false).Value?.Rows.Count ?? 0;
        var added = _admin.Add(new KeyInput(1, args.Get("--label"), token, args.Get("--account"), KeySources.Local));
        if (added.Value is not { } row)
        {
            return Refused(added);
        }

        if (before == 0 && _admin.SharedSentence.Length > 0)
        {
            runtime.Note(_admin.SharedSentence);
        }

        runtime.Output.WriteLine(args.Json
            ? KeysMode.Line(new KeyAddedAnswer(row.Id, row.Label, row.State, row.AddedUtc), KeysAnswerJson.Default.KeyAddedAnswer)
            : $"added {row.Id}  {row.Label}");

        return 0;
    }

    private KeyResult<string> ReadKeyLine()
    {
        if (!runtime.StdinIsTerminal)
        {
            return KeyStdin.ReadLine(runtime.Stdin);
        }

        runtime.Prompt("Paste the key (claude setup-token), then press Enter — it is not shown: ");
        var typed = runtime.ReadHidden();

        return string.IsNullOrWhiteSpace(typed) ? KeyResult<string>.Refused("no text was typed") : KeyResult<string>.Of(typed.Trim());
    }

    // -------------------------------------------------------------------------------------------------------------
    // import
    // -------------------------------------------------------------------------------------------------------------

    public int Import()
    {
        var format = args.Has("--format") ? args.Get("--format") : "lines";
        if (format is not ("lines" or "env" or "creds-json"))
        {
            runtime.Note("--keys import: --format is lines, env or creds-json");

            return KeyExit.Data;
        }

        if (args.Has("--source") && format == "lines")
        {
            runtime.Note("--keys import: --source belongs to --format env or creds-json (the sources that are synced)");

            return KeyExit.Data;
        }

        return format == "lines" ? ImportLines() : ImportSynced(format);
    }

    private int ImportLines()
    {
        var text = KeyStdin.ReadAll(runtime.Stdin, KeyStdin.MaxImportLines * KeyStdin.MaxImportLineBytes);
        if (text.Value is not { } all)
        {
            return Refused(text);
        }

        var lines = all.Split('\n').Select((line, index) => (Number: index + 1, Text: line.TrimEnd('\r'))).Where(line => line.Text.Trim().Length > 0).ToList();
        if (lines.Count == 0 || lines.Count > KeyStdin.MaxImportLines)
        {
            runtime.Note(lines.Count == 0 ? "--keys import: no lines came on stdin" : $"--keys import: at most {KeyStdin.MaxImportLines} lines; nothing was imported");

            return KeyExit.Data;
        }

        var inputs = new List<KeyInput>();
        var refused = new List<KeyRefusedLine>();
        foreach (var (number, line) in lines)
        {
            if (ImportLine.Parse(number, line) is { } input)
            {
                inputs.Add(input);
            }
            else
            {
                refused.Add(new KeyRefusedLine(number, ImportLine.Rule));
            }
        }

        var done = _admin.Import(inputs);
        if (done.Value is not { } counts)
        {
            return Refused(done);
        }

        return Print(counts with { Refused = [.. refused, .. counts.Refused] });
    }

    private int ImportSynced(string format)
    {
        var source = args.Has("--source") ? args.Get("--source") : "import:" + format;
        if (!SourceName().IsMatch(source) || source == KeySources.Local || source == KeySources.Environment || source.StartsWith(KeySources.FilePrefix, StringComparison.Ordinal))
        {
            runtime.Note("--keys import: --source is a name of 1 to 80 of a-z 0-9 : _ . / - that is not local, env or file:…");

            return KeyExit.Data;
        }

        var text = KeyStdin.ReadAll(runtime.Stdin, KeyStdin.MaxSourceBytes);
        if (text.Value is not { } all)
        {
            return Refused(text);
        }

        var read = format == "env" ? KeyResult<SourceRead>.Of(KeySourceReader.FromEnvLines(all, source)) : KeySourceReader.FromCredsJson(all, source);
        if (read.Value is not { } listed)
        {
            return Refused(read);
        }

        var done = new KeySync(access, _admin).Apply(source, listed);

        return done.Value is { } counts ? Print(counts) : Refused(done);
    }

    private int Print(KeyImportAnswer counts)
    {
        runtime.Output.WriteLine(args.Json
            ? KeysMode.Line(counts, KeysAnswerJson.Default.KeyImportAnswer)
            : $"added {counts.Added}, already there {counts.AlreadyThere}, replaced {counts.Replaced}, disabled {counts.Disabled}, enabled {counts.Enabled}, refused {counts.Refused.Count}"
                + string.Concat(counts.Refused.Select(line => $"\nrefused line {line.Line}: {line.Reason}")));

        return counts.Refused.Count > 0 && counts.Added + counts.AlreadyThere + counts.Replaced == 0 ? KeyExit.Data : 0;
    }

    // -------------------------------------------------------------------------------------------------------------
    // relabel, disable, enable, remove
    // -------------------------------------------------------------------------------------------------------------

    public int Change()
    {
        if (!args.Has("--id") || (args.Verb == "relabel" && !args.Has("--label")))
        {
            runtime.Note($"--keys {args.Verb} needs --id <key id>{(args.Verb == "relabel" ? " and --label <name>" : string.Empty)}");

            return KeyExit.Data;
        }

        int? revision = null;
        if (args.Has("--expect-revision"))
        {
            if (!int.TryParse(args.Get("--expect-revision"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                runtime.Note("--keys: --expect-revision is the whole number the list showed");

                return KeyExit.Data;
            }

            revision = parsed;
        }

        return args.Verb == "remove" ? Removed(_admin.Remove(args.Get("--id"), revision)) : Row(Changed(revision));
    }

    private KeyResult<KeyRow> Changed(int? revision) => args.Verb switch
    {
        "relabel" => _admin.Relabel(args.Get("--id"), args.Get("--label"), revision),
        "disable" => _admin.Disable(args.Get("--id"), revision),
        _ => _admin.Enable(args.Get("--id"), revision),
    };

    private int Row(KeyResult<KeyRow> changed)
    {
        if (changed.Value is not { } row)
        {
            return Refused(changed);
        }

        runtime.Output.WriteLine(args.Json ? KeysMode.Line(row, KeysAnswerJson.Default.KeyRow) : $"{row.Id}  {row.Label}  {row.State}");

        return 0;
    }

    private int Removed(KeyResult<KeyRemovedAnswer> removed)
    {
        if (removed.Value is not { } answer)
        {
            return Refused(removed);
        }

        runtime.Output.WriteLine(args.Json ? KeysMode.Line(answer, KeysAnswerJson.Default.KeyRemovedAnswer) : $"removed {answer.Removed}");

        return 0;
    }

    // -------------------------------------------------------------------------------------------------------------
    // scan, sync
    // -------------------------------------------------------------------------------------------------------------

    public int Scan()
    {
        if (!args.Has("--path"))
        {
            runtime.Note("--keys scan needs --path <folder or file>");

            return KeyExit.Data;
        }

        var scanned = KeyScanner.Scan(access, args.Get("--path"));
        if (scanned.Value is not { } answer)
        {
            return Refused(scanned);
        }

        runtime.Output.WriteLine(args.Json
            ? KeysMode.Line(answer, KeysAnswerJson.Default.KeyScanAnswer)
            : answer.Hits.Count == 0
                ? $"no hit ({answer.Scanned} file(s) read, {answer.Unreadable} not readable)"
                : string.Join('\n', answer.Hits.Select(hit => $"{hit.Path}  {hit.KeyId}")));

        return 0;
    }

    public int Sync()
    {
        var answer = KeySourceSync.Run(access, Layered, runtime.Variables);
        runtime.Output.WriteLine(args.Json
            ? KeysMode.Line(answer, KeysAnswerJson.Default.KeySyncAnswer)
            : string.Join('\n', answer.Sources.Select(source => source.Counts is { } counts
                ? $"{source.Source}: added {counts.Added}, replaced {counts.Replaced}, disabled {counts.Disabled}, enabled {counts.Enabled}, refused {counts.Refused.Count}"
                : $"{source.Source}: refused — {source.Refusal}")));

        return 0;
    }

    private int Refused<T>(KeyResult<T> result)
    {
        runtime.Note($"--keys {args.Verb}: {result.Refusal}");

        return result.Code == 0 ? KeyExit.Data : result.Code;
    }
}

/// <summary>One line of <c>--keys import</c>: <c>{"label","token"}</c> and optionally <c>"account"</c>. Anything else is refused by the rule, never by the line.</summary>
internal static class ImportLine
{
    internal const string Rule = "the line must be one JSON object with `label` and `token` (and optionally `account`), all text";

    public static KeyInput? Parse(int number, string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(property => property.Name is not ("label" or "token" or "account") || property.Value.ValueKind != JsonValueKind.String))
            {
                return null;
            }

            string Text(string name) => root.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

            return root.TryGetProperty("label", out _) && root.TryGetProperty("token", out _)
                ? new KeyInput(number, Text("label"), Text("token"), Text("account"), KeySources.Local)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
