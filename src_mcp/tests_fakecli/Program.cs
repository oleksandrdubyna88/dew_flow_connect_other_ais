// The scriptable stand-in for a reviewer CLI. Verbs:
//
//   [count <file>] <verb> <args...>      — `count` first: append one line per launch, then run the verb
//
//   emit <text>                          — text to stdout, exit 0
//   emit-to <dst> <text>                 — text into the file (codex -o), nothing on stdout, exit 0
//   stderr-exit <text> <code>            — text to stderr, exit <code>
//   sleep <ms>                           — exit 0 after the delay
//   busy <dir> <ms>                      — write <guid>.start (ticks), sleep, write <guid>.end (ticks)
//   flip <flag> <firstStderr> <firstExit> <thenStdout>
//                                        — first launch: create <flag>, stderr, exit <firstExit>;
//                                          later launches: <thenStdout> to stdout, exit 0
//   fail-until <counter> <n> <stderr> <exit> <thenStdout>
//                                        — while <counter> has <n> lines or fewer: stderr, exit
//                                          <exit>; after that: <thenStdout> to stdout, exit 0.
//                                          Pair it with the `count <counter>` prefix, which is what
//                                          writes those lines. `flip` is the same idea for n=1 and
//                                          stays because half the suite uses it; a ladder needs a
//                                          stand-in that can fail more than once.
//
// Deliberately dumb: every behaviour a test needs, none it does not.

// UTF-8 out, like the real vendors (node writes UTF-8). Without this Windows encodes stdout in
// the console's code page and every Cyrillic character leaves as '?' — which is exactly how the
// launcher's own missing StandardOutputEncoding was found.
Console.OutputEncoding = System.Text.Encoding.UTF8;
// And UTF-8 IN, before `Console.In` is first touched: the launcher writes the prompt BOM-less UTF-8
// and every real vendor reads it as such. Decoded in the console code page instead, a recorded prompt
// lost every em dash, so an assertion over a served tail (S3.2) had to stay ASCII to pass.
Console.InputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// A stand-in that dies unhandled destroys the evidence it exists to produce. Windows CI failed a
// full loop twice with `exit -532462766` — 0xE0434352, "a managed exception escaped" — and all the
// executor could keep was the TAIL of the runtime's stack dump, which ends inside a `FileStream`
// constructor's parameter list and names neither the exception nor the path. One line, written
// last, is worth the whole dump: the tail is what survives.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var error = e.ExceptionObject as Exception;
    Console.Error.WriteLine(
        $"fake-cli: {error?.GetType().Name ?? "unknown"}: {error?.Message ?? "no message"}");
    Console.Error.Flush();
    // Before the runtime prints its own dump, so our line is the last thing on the stream rather
    // than the first thing scrolled off it.
    Environment.Exit(97);
};

// Vendor mode: behave like a reviewer CLI whatever the argv shape, steered by environment —
// the vendor runtimes build real codex/gemini argvs, and the fan-out tests drive THOSE.
//   FAKECLI_MODE=vendor
//   FAKECLI_STDOUT       — text for stdout (the gemini path)
//   FAKECLI_OUTFILE_TEXT — text for the file after `-o` in argv (the codex path)
//   FAKECLI_STDERR / FAKECLI_EXIT — failure steering
//   FAKECLI_SLEEP_MS     — answer only after this long (the probe's timeout arm)
//   FAKECLI_SIDE_EFFECT  — write a file at this path: a vendor breaking its own read-only promise
//   FAKECLI_RECORD_DIR   — write each launch's full argv into <guid>.argv there
//   FAKECLI_HELP_STDOUT / FAKECLI_HELP_EXIT
//                        — what a bare `--help` prints and exits with (a claude's capability probe); stdin is
//                          not read for it. Unset: the help prints nothing useful and exits 0
//
// Two placeholders are filled in FAKECLI_STDOUT and FAKECLI_OUTFILE_TEXT (the consultant check, epic 4 — its
// marker and canary are random, so the answer cannot be scripted in advance):
//   {{cwd-file:NAME}}      — the trimmed text of NAME in the working directory (a file the consultant READ)
//   {{path-in-prompt:SUFFIX}} — that absolute path itself, with forward slashes (FAKECLI_SIDE_EFFECT takes it too)
//   {{prompt-path:SUFFIX}} — the trimmed text of the absolute path in the prompt that ends in SUFFIX (a read
//                            OUTSIDE the repository — the leak a confined CLI must refuse)
//
// The TURN family (S3.2 of the feature-review plan) — a reviewer that is asked again with its source
// served must answer differently the second time, and which turn a launch IS can only be read off
// the prompt it was handed, because two reviewers of one round interleave on any counter:
//   FAKECLI_TURN_MARKER    — a text with `{n}` in it ("## Turn {n} of"); the launch is turn n when its
//                            prompt (stdin, or the file after `--prompt-file`) contains it with n
//                            filled in, for the smallest such n from 2; else turn 1
//   FAKECLI_REPAIR_MARKER  — a text; the launch is a REPAIR when its prompt contains it
//   FAKECLI_TURN<n>_STDOUT / _OUTFILE_TEXT / _EXIT / _STDERR / _SLEEP_MS
//                          — turn n's steering, each falling back to the un-numbered variable
//   FAKECLI_TURN<n>_REPAIR_STDOUT / _OUTFILE_TEXT / _EXIT / _STDERR / _SLEEP_MS
//                          — the same for turn n's repair launch, falling back to turn n's, then the bare one
// A child started with the MINIMAL environment (a question row, PLAN_question_consultant.md S1 acceptance 6)
// sees no FAKECLI_* variable at all — TEMP is on the list and nothing of a test's is — so the vendor mode
// can also be steered by a FILE in the temp directory, read only when the environment says nothing and the
// argv is a vendor's shape. The same names as the variables, so one test scripts both roads alike.
var minimal = MinimalSteering.Load(args);
Func<string, string?> read = minimal is { } fromFile ? name => fromFile.GetValueOrDefault(name) : Environment.GetEnvironmentVariable;
if (Environment.GetEnvironmentVariable("FAKECLI_MODE") == "vendor" || minimal is not null)
{
    // A claude consultant asks its CLI's --help before every turn; this answers it without touching stdin.
    if (args is ["--help"])
    {
        Console.Out.Write(read("FAKECLI_HELP_STDOUT") ?? "Usage: fake [options]\n");
        return int.TryParse(read("FAKECLI_HELP_EXIT"), out var helpExit) ? helpExit : 0;
    }

    // Raw stdin, byte for byte, before any decoder can tidy it up. This exists because a
    // decoded string cannot answer "was there a byte-order mark in front of the prompt" — the
    // Console decoder strips it, which is exactly how three stray bytes went unnoticed for a
    // whole product.
    if (read("FAKECLI_RECORD_STDIN_BYTES") is { Length: > 0 } bytesPath)
    {
        using var input = Console.OpenStandardInput();
        using var file = File.Create(bytesPath);
        input.CopyTo(file);
        return 0;
    }

    // Read ONCE: the recorder and the turn family both want it, and stdin does not rewind.
    var prompt = PromptOf(args);

    if (read("FAKECLI_RECORD_DIR") is { Length: > 0 } record)
    {
        // NUL-joined, because a recorded field may be multiline (the prompt on stdin) — lines
        // cannot reconstruct an argv, a character no argv contains can. The stdin text is
        // recorded as the LAST field, since that is where the prompt lives now.
        File.WriteAllText(
            Path.Combine(record, $"{Guid.NewGuid():N}.argv"),
            string.Join('\0', args.Append(prompt.StdIn)));
    }

    var steering = Steering.For(prompt.Text, read);

    // A vendor that takes its time. It exists for the health probe's timeout arm: a CLI that
    // never answers `--version` must be reported as silent rather than as whatever exit code the
    // kill produced, and that cannot be tested by a stand-in which always answers at once.
    if (int.TryParse(steering.Get("SLEEP_MS"), out var napMs) && napMs > 0)
    {
        Thread.Sleep(napMs);
    }

    var stderrText = steering.Get("STDERR");
    if (stderrText is { Length: > 0 })
    {
        Console.Error.WriteLine(stderrText);
    }

    // `-o` means two different things to two vendors. Codex takes `-o <absolute path>.json` — where
    // to WRITE the answer. Gemini takes `-o json` — what FORMAT to answer in. Reading every `-o` as
    // codex's made a gemini-shaped launch write a file literally called `json`, in its working
    // directory — which every reviewer of a round shares. Three gemini reviewers then opened one
    // file at once, and on Windows, where a share mode is enforced rather than advisory, the losers
    // died: two release attempts lost to `exit -532462766`, never reproducible on a developer
    // machine. A rooted path is the one thing that separates a destination from a format name.
    var outIndex = Array.IndexOf(args, "-o");
    if (outIndex >= 0 && outIndex + 1 < args.Length && Path.IsPathRooted(args[outIndex + 1]) &&
        steering.Get("OUTFILE_TEXT") is { Length: > 0 } fileText)
    {
        File.WriteAllText(args[outIndex + 1], Placeholders.Fill(fileText, prompt.Text));
    }

    if (steering.Get("STDOUT") is { Length: > 0 } stdoutText)
    {
        Console.Out.Write(Placeholders.Fill(stdoutText, prompt.Text));
    }

    // A vendor that writes where it was told not to. A consultant runs in the LIVE working tree
    // behind three read-only flags that are the VENDOR's promise rather than ours, so the filesystem
    // invariant has to be testable against a child that actually breaks one.
    if (read("FAKECLI_SIDE_EFFECT") is { Length: > 0 } sideEffect)
    {
        File.WriteAllText(Placeholders.Fill(sideEffect, prompt.Text), "written by a CLI that promised to be read-only\n");
    }

    return int.TryParse(steering.Get("EXIT"), out var exit) ? exit : 0;
}

var args0 = args;
if (args0.Length >= 2 && args0[0] == "count")
{
    using (var counter = new FileStream(args0[1], FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
    using (var writer = new StreamWriter(counter))
    {
        writer.WriteLine("L");
    }

    args0 = args0[2..];
}

switch (args0)
{
    case ["emit", var text]:
        Console.Out.Write(text);
        return 0;

    case ["emit-to", var dst, var text]:
        File.WriteAllText(dst, text);
        return 0;

    // Says something on stderr and NOTHING useful on stdout, exiting 0 — the shape of a vendor
    // whose envelope came back empty while its streams carried the diagnosis.
    case ["stderr-emit", var noise, var text]:
        Console.Error.WriteLine(noise);
        Console.Out.Write(text);
        return 0;

    case ["stderr-exit", var text, var code]:
        Console.Error.WriteLine(text);
        return int.Parse(code);

    // Says something on STDOUT — a usage line, typically — and exits non-zero: the shape of a shim that
    // was billed for a call and then refused its answer (a reasoning-only completion, a cut at the
    // token ceiling), whose cost must still cross the process boundary.
    case ["emit-exit", var text, var code]:
        Console.Out.Write(text);
        return int.Parse(code);

    case ["sleep", var ms]:
        Thread.Sleep(int.Parse(ms));
        return 0;

    // Reads all of stdin and writes it back, byte for byte. The POSITIVE companion to `sleep`: one
    // proves a child that never reads is still killed on time, this one proves the launcher still
    // delivers every byte to a child that does. Raw streams on both sides, because a decoder in the
    // middle would make it a test of the decoder.
    case ["echo-stdin"]:
        using (var stdin = Console.OpenStandardInput())
        using (var stdout = Console.OpenStandardOutput())
        {
            stdin.CopyTo(stdout);
        }

        return 0;

    // Every environment variable NAME this child was started with, one per line. The only way to ask
    // what a launch actually handed a process: a dictionary asserted in-process is a test of the
    // dictionary, and what is being measured here is what crossed the process boundary.
    case ["env-names"]:
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(StringComparer.Ordinal))
        {
            Console.Out.WriteLine(name);
        }

        return 0;

    // <n> characters to stdout with NO newline among them, then exit. A line-based reader cannot see
    // this arriving at all — it delivers nothing until the stream closes, by which time the whole
    // runaway has been buffered, which is why the output ceiling could not stay on lines.
    case ["spew", var count]:
        var chunk = new string('x', 4096);
        for (var written = 0; written < int.Parse(count); written += chunk.Length)
        {
            Console.Out.Write(chunk);
        }

        Console.Out.Flush();
        return 0;

    case ["busy", var dir, var ms]:
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(dir, $"{id}.start"), DateTime.UtcNow.Ticks.ToString());
        Thread.Sleep(int.Parse(ms));
        File.WriteAllText(Path.Combine(dir, $"{id}.end"), DateTime.UtcNow.Ticks.ToString());
        return 0;

    // Takes the REAL EngineLease in its own process, holds it, and prints the window it held it
    // for. Five of these are how the cross-process claim is measured rather than asserted: two
    // objects in one process would prove nothing about two servers on one machine.
    case ["lease", var leaseDir, var holdMs, var _]:
        CoaiMcp.Runners.Reviewers.EngineLease.Directory = leaseDir;
        using (var lease = CoaiMcp.Runners.Reviewers.EngineLease
            .AcquireAsync("http://127.0.0.1:11434/v1", DateTime.UtcNow.AddSeconds(60))
            .GetAwaiter()
            .GetResult())
        {
            if (lease is null)
            {
                Console.Error.WriteLine("fake-cli: the card never came free");
                return 69;
            }
            var start = DateTime.UtcNow;
            Thread.Sleep(int.Parse(holdMs));
            Console.Out.WriteLine($"{start:O} {DateTime.UtcNow:O}");
        }

        return 0;

    case ["flip", var flag, var firstStderr, var firstExit, var thenStdout]:
        if (!File.Exists(flag))
        {
            File.WriteAllText(flag, "flipped");
            Console.Error.WriteLine(firstStderr);
            return int.Parse(firstExit);
        }

        Console.Out.Write(thenStdout);
        return 0;

    case ["fail-until", var counter, var times, var stderrText, var exitCode, var thenStdout]:
        // The counter is read, never written, here: the `count` prefix above has already appended
        // this launch's line, so a launch sees its own attempt number.
        if (LinesIn(counter) <= int.Parse(times))
        {
            Console.Error.WriteLine(stderrText);
            return int.Parse(exitCode);
        }

        Console.Out.Write(thenStdout);
        return 0;

    default:
        Console.Error.WriteLine($"fake-cli: unknown verb [{string.Join(' ', args0)}]");
        return 64;
}

/// <summary>
/// How many launches the counter file has recorded — with a share mode that tolerates the writer
/// above, since on Windows a plain read of a file another handle has open throws.
/// </summary>
static int LinesIn(string path)
{
    try
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }
    catch (FileNotFoundException)
    {
        return 0;
    }
}

/// <summary>
/// The prompt this launch was handed — stdin, read whole, and the file after <c>--prompt-file</c> when
/// the argv names one (the api shim's shape). The stdin half is kept apart because the recorder writes
/// exactly what arrived on stdin, and a prompt read from a file did not.
/// </summary>
static (string StdIn, string Text) PromptOf(string[] args)
{
    var stdin = Console.IsInputRedirected ? Console.In.ReadToEnd() : string.Empty;
    var at = Array.IndexOf(args, "--prompt-file");
    var fromFile = at >= 0 && at + 1 < args.Length && File.Exists(args[at + 1]) ? File.ReadAllText(args[at + 1]) : string.Empty;

    return (stdin, stdin.Length > 0 ? stdin : fromFile);
}

/// <summary>
/// Which environment variables steer THIS launch: turn n's, its repair's, or the bare ones — read off
/// the prompt, never off a counter, because two reviewers of one round interleave on any counter.
/// </summary>
sealed class Steering
{
    private readonly string[] _prefixes;
    private readonly Func<string, string?> _read;

    private Steering(string[] prefixes, Func<string, string?> read) => (_prefixes, _read) = (prefixes, read);

    /// <summary>The first of the prefixes that names a value — most specific first.</summary>
    public string? Get(string name) =>
        _prefixes.Select(prefix => _read(prefix + name)).FirstOrDefault(value => value is { Length: > 0 });

    /// <param name="read">Where a steering value comes from: the environment, or the minimal-environment file.</param>
    public static Steering For(string prompt, Func<string, string?> read)
    {
        var turn = TurnOf(prompt, read);
        var repair = read("FAKECLI_REPAIR_MARKER") is { Length: > 0 } marker
                     && prompt.Contains(marker, StringComparison.Ordinal);
        // Turn 1 reads `FAKECLI_TURN1_*` too, so a test can script every turn the same way.
        string[] prefixes = repair
            ? [$"FAKECLI_TURN{turn}_REPAIR_", $"FAKECLI_TURN{turn}_", "FAKECLI_"]
            : [$"FAKECLI_TURN{turn}_", "FAKECLI_"];

        return new Steering(prefixes, read);
    }

    /// <summary>Turn n when the prompt carries the marker with n filled in, for the smallest n from 2 up to 16; else 1.</summary>
    private static int TurnOf(string prompt, Func<string, string?> read)
    {
        if (read("FAKECLI_TURN_MARKER") is not { Length: > 0 } template)
        {
            return 1;
        }

        return Enumerable.Range(2, 15)
            .FirstOrDefault(n => prompt.Contains(template.Replace("{n}", n.ToString(), StringComparison.Ordinal), StringComparison.Ordinal), 1);
    }
}

/// <summary>
/// The vendor mode's steering for a child that was started with a MINIMAL environment: a JSON object of
/// the same `FAKECLI_*` names, in the temp directory — the one place such a child can still find.
/// </summary>
/// <remarks>
/// Read ONLY when the environment carries no mode and the argv is a vendor's shape (codex's `exec` or
/// `--search`, claude's `-p`, the api shim's `--ask-api`): a verb-mode launch is never redirected, and
/// a vendor-mode launch steered by its environment never reads the file. The test that writes it deletes it.
/// </remarks>
static class MinimalSteering
{
    public const string FileName = "fakecli-minimal.json";

    public static Dictionary<string, string>? Load(string[] args)
    {
        if (Environment.GetEnvironmentVariable("FAKECLI_MODE") is { Length: > 0 } || args.Length == 0 || !IsVendorShape(args))
        {
            return null;
        }

        var path = Path.Combine(Path.GetTempPath(), FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var parsed = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllText(path), FakeCliJsonContext.Default.DictionaryStringString);

        return parsed is null ? null : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
    }

    private static bool IsVendorShape(string[] args) =>
        args[0] is "exec" or "--search" or "-p" || args.Contains("--ask-api", StringComparer.Ordinal);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "DictionaryStringString")]
sealed partial class FakeCliJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

/// <summary>
/// The three placeholders a scripted answer may carry: a file in the working directory, a file the PROMPT names by
/// an absolute path, and that path itself — so a check whose words are random can still be answered by a stand-in that
/// "read" them, or wrote one.
/// </summary>
static class Placeholders
{
    public static string Fill(string text, string prompt) =>
        System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\{\{(cwd-file|prompt-path|path-in-prompt):([^}]+)\}\}",
            match => match.Groups[1].Value switch
            {
                "cwd-file" => Read(Path.Combine(Environment.CurrentDirectory, match.Groups[2].Value)),
                "prompt-path" => Read(NamedIn(prompt, match.Groups[2].Value)),
                // The PATH itself, with forward slashes, so it sits in a JSON string unescaped on any platform.
                _ => NamedIn(prompt, match.Groups[2].Value).Replace('\\', '/'),
            },
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));

    /// <summary>The first rooted path in the prompt that ends in <paramref name="suffix"/> — or empty.</summary>
    private static string NamedIn(string prompt, string suffix) =>
        prompt.Split([' ', '\n', '\r', '\t', '`', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.TrimEnd('.', ',', ';', ')'))
            .FirstOrDefault(token => Path.IsPathRooted(token) && token.EndsWith(suffix, StringComparison.Ordinal)) ?? string.Empty;

    private static string Read(string path) => path.Length > 0 && File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
}
