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
//
// Deliberately dumb: every behaviour a test needs, none it does not.

// UTF-8 out, like the real vendors (node writes UTF-8). Without this Windows encodes stdout in
// the console's code page and every Cyrillic character leaves as '?' — which is exactly how the
// launcher's own missing StandardOutputEncoding was found.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Vendor mode: behave like a reviewer CLI whatever the argv shape, steered by environment —
// the vendor runtimes build real codex/gemini argvs, and the fan-out tests drive THOSE.
//   FAKECLI_MODE=vendor
//   FAKECLI_STDOUT       — text for stdout (the gemini path)
//   FAKECLI_OUTFILE_TEXT — text for the file after `-o` in argv (the codex path)
//   FAKECLI_STDERR / FAKECLI_EXIT — failure steering
//   FAKECLI_RECORD_DIR   — write each launch's full argv into <guid>.argv there
if (Environment.GetEnvironmentVariable("FAKECLI_MODE") == "vendor")
{
    if (Environment.GetEnvironmentVariable("FAKECLI_RECORD_DIR") is { Length: > 0 } record)
    {
        // NUL-joined, because a recorded field may be multiline (the prompt on stdin) — lines
        // cannot reconstruct an argv, a character no argv contains can. The stdin text is
        // recorded as the LAST field, since that is where the prompt lives now.
        var stdin = Console.IsInputRedirected ? Console.In.ReadToEnd() : string.Empty;
        File.WriteAllText(
            Path.Combine(record, $"{Guid.NewGuid():N}.argv"),
            string.Join('\0', args.Append(stdin)));
    }

    var stderrText = Environment.GetEnvironmentVariable("FAKECLI_STDERR");
    if (stderrText is { Length: > 0 })
    {
        Console.Error.WriteLine(stderrText);
    }

    var outIndex = Array.IndexOf(args, "-o");
    if (outIndex >= 0 && outIndex + 1 < args.Length &&
        Environment.GetEnvironmentVariable("FAKECLI_OUTFILE_TEXT") is { Length: > 0 } fileText)
    {
        File.WriteAllText(args[outIndex + 1], fileText);
    }

    if (Environment.GetEnvironmentVariable("FAKECLI_STDOUT") is { Length: > 0 } stdoutText)
    {
        Console.Out.Write(stdoutText);
    }

    return int.TryParse(Environment.GetEnvironmentVariable("FAKECLI_EXIT"), out var exit) ? exit : 0;
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

    case ["stderr-exit", var text, var code]:
        Console.Error.WriteLine(text);
        return int.Parse(code);

    case ["sleep", var ms]:
        Thread.Sleep(int.Parse(ms));
        return 0;

    case ["busy", var dir, var ms]:
        var id = Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(dir, $"{id}.start"), DateTime.UtcNow.Ticks.ToString());
        Thread.Sleep(int.Parse(ms));
        File.WriteAllText(Path.Combine(dir, $"{id}.end"), DateTime.UtcNow.Ticks.ToString());
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

    default:
        Console.Error.WriteLine($"fake-cli: unknown verb [{string.Join(' ', args0)}]");
        return 64;
}
