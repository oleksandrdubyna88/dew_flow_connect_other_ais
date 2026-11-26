using System.Text.Json;

namespace CoaiMcp.FakeCli;

/// <summary>
/// <c>FAKECLI_MODEL_MAP</c> (epoch 3 E1 s3): ONE file that says what the fake answers for each <c>--model</c> (or <c>-m</c>) it is launched with — so a failover test scripts a whole route in one place: the first model
/// is limited, the second crashes, the third answers. Vendor mode only; a model that has no entry and no <c>"*"</c> entry is steered by the ordinary <c>FAKECLI_*</c> variables as before.
/// </summary>
/// <remarks>
/// The file is a JSON object of model name to an object — or to an ARRAY of objects, the k-th launch of that model taking the k-th (the last repeats), counted in a file beside the map — with any of <c>stdout</c>, <c>stderr</c>, <c>exit</c> (a number), <c>sleepMs</c> (a number) and <c>outfileText</c> (what codex's <c>-o</c> file holds), and <c>writeFiles</c> (an object of relative path to text, written below the working folder before the fake answers — a coder's edits, <see cref="CwdWrites"/>) and <c>tryWriteFiles</c> (an array of ABSOLUTE paths the fake tries to write, outside the working folder: a coder that tries to
/// write where it must not — what it could not write goes to stderr as <c>fake-cli: could not write &lt;path&gt;: &lt;the OS's words&gt;</c>, nothing to stdout, and the answer follows as if nothing happened, <see cref="CoderVerbs.TryWrite"/>). The name <c>"*"</c> is
/// the entry for any model the file does not name. The path is read through the same lookup as every other steering value, so the minimal-environment file can carry it too.
/// </remarks>
internal static class ModelMap
{
    /// <summary>The value of a steering name (<c>STDOUT</c>, <c>STDERR</c>, <c>EXIT</c>, <c>SLEEP_MS</c>, <c>OUTFILE_TEXT</c>) for the launch's model, or null when the file has no entry for it.</summary>
    public static Func<string, string?>? For(string[] args, Func<string, string?> read)
    {
        if (read("FAKECLI_MODEL_MAP") is not { Length: > 0 } path || !File.Exists(path) || ModelOf(args) is not { } model)
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var entry = document.RootElement.TryGetProperty(model, out var named) ? named
            : document.RootElement.TryGetProperty("*", out var any) ? any
            : default;
        if (entry.ValueKind == JsonValueKind.Array && entry.GetArrayLength() > 0)
        {
            entry = entry[Math.Min(Launched(path, model, read), entry.GetArrayLength() - 1)];
        }

        entry = PoolKey.Choose(entry);

        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (steering, field) in new[] { ("STDOUT", "stdout"), ("STDERR", "stderr"), ("EXIT", "exit"), ("SLEEP_MS", "sleepMs"), ("OUTFILE_TEXT", "outfileText"), ("WRITE_FILES", "writeFiles"), ("TRY_WRITE_FILES", "tryWriteFiles"), ("HOME_FILES", "homeFiles"), ("READ_FILES", "readFiles"), ("IGNORE_TERM", "ignoreTerm") })
        {
            if (entry.TryGetProperty(field, out var value))
            {
                values[steering] = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
            }
        }

        return name => values.GetValueOrDefault(name);
    }

    /// <summary>How many times this model was launched before now — a line is added for this launch. Launches of one test are sequential, so a plain append is enough.</summary>
    private static int Launched(string path, string model, Func<string, string?> read)
    {
        var name = string.Concat(model.Select(c => char.IsLetterOrDigit(c) ? c : '_')) + ".count";
        // FAKECLI_STATE_DIR: the counter lives in that folder (the unit's own temp folder), not beside the map in the worktree, where the clean before every round of checks would remove it.
        var counter = read("FAKECLI_STATE_DIR") is { Length: > 0 } stateDir ? Path.Combine(stateDir, Path.GetFileName(path) + "." + name) : path + "." + name;
        var before = File.Exists(counter) ? File.ReadAllLines(counter).Length : 0;
        File.AppendAllText(counter, "L\n");

        return before;
    }

    /// <summary>The model a vendor-shaped argv names: the value after <c>--model</c> or <c>-m</c>.</summary>
    private static string? ModelOf(string[] args)
    {
        var at = Array.FindIndex(args, arg => arg is "--model" or "-m");

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
