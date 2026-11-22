using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server;

namespace CoaiMcp;

/// <summary>
/// <c>coai-mcp --band-json</c> — what the Claude mod's band shows, as ONE JSON object on stdout:
/// <c>{"phrases":[],"consultations":[...],"failures":[...]}</c>, read from the local data folder, then exit 0
/// (v2 E3.S4; <c>v2/architecture/extension.md</c> §6 and §10 row 5). The mod runs it on a 5 s timer.
/// </summary>
/// <remarks>
/// <para><b>A reader, and it never makes the data directory.</b> It opens no home, no lock, no network and no database; it lists the
/// consultation and question files and reads the tail of the notices file. A folder that is not there answers three empty lists.
/// <c>Program.WriterModes</c> does not hold it, and <c>AReaderModeNeverMakesTheDataDirectoryTests</c> runs it against a missing named root.</para>
/// <para><b>Phrases are empty from here, and that is a statement, not a gap.</b> The phrases are a person's own text and live in VS Code's
/// user settings (<c>coai.phrases</c>, read by the extension's <c>phrases.ts</c>); they are deliberately not mirrored to the server
/// (<c>phrases.ts</c>: "Person-level, and it does not cross to the server"), so there is no file here to read them from. The list is
/// kept in the object so the mod's contract is the one in the design; a person-level phrase file, if one ever exists, is read here.</para>
/// <para><b>Consultations</b> are the ones in flight: a <c>cons</c> record <c>asking</c> or <c>open</c>, a <c>consask</c> record
/// <c>consulting</c>, most recently touched first. A record the sweep has not yet closed is shown as the disk has it — a reader does
/// not sweep.</para>
/// <para><b>Failures</b> are the server's own notices of class <c>failure</c> written in the last <see cref="FailureWindow"/>, newest first,
/// read from the last <see cref="TailBytes"/> of <c>server-notices.jsonl</c>. The notices file has no "acknowledged" state, so "open"
/// is "recent": a failure leaves the band a day after it was written. A torn line is skipped.</para>
/// <para><b>Exit codes.</b> 0 with the object; <b>65</b> for any argument after the mode (the sentence on stderr); <b>74</b> for a file
/// the disk will not read. Never 64 — that is "never heard of this mode", how the mod takes an older binary.</para>
/// </remarks>
internal static class BandJsonMode
{
    internal const string Usage = "--band-json takes no other argument";

    /// <summary>How long a failure stays in the band.</summary>
    internal static readonly TimeSpan FailureWindow = TimeSpan.FromHours(24);

    /// <summary>How much of the end of the notices file is read — about 2,500 notices at the typical 400 B.</summary>
    internal const int TailBytes = 1024 * 1024;

    /// <summary>The most rows each list carries; a band is a strip, not a log.</summary>
    internal const int MostRows = 20;

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static int Run(string[] args, Func<string, string?> env, TextWriter output, Action<string> note, TimeProvider clock)
    {
        if (args.Length != 1)
        {
            note(Usage);

            // 65, NOT 64: this binary HAS the mode and is refusing the request.
            return 65; // EX_DATAERR
        }

        try
        {
            output.WriteLine(Answer(PanelSettings.FromEnvironment(env).DataDir, clock.GetUtcNow()));

            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note($"--band-json could not read the data directory: {e.Message}");

            return 74; // EX_IOERR
        }
    }

    /// <summary>The object, on one line.</summary>
    internal static string Answer(string dataDir, DateTimeOffset now)
    {
        var consultations = Consultations(dataDir);
        var failures = Failures(dataDir, now);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteStartArray("phrases");
            json.WriteEndArray();
            json.WriteStartArray("consultations");
            foreach (var one in consultations)
            {
                one.WriteTo(json);
            }

            json.WriteEndArray();
            json.WriteStartArray("failures");
            foreach (var one in failures)
            {
                one.WriteTo(json);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static List<JsonElement> Consultations(string dataDir)
    {
        var consults = new ConsultationStore(dataDir).All()
            .Where(record => record.Status is ConsultationStatuses.Asking or ConsultationStatuses.Open)
            .Select(record => (Touched: Touched(record.UpdatedUtc, record.StartedUtc), Row: Row(
                record.Id, "cons", record.Status, record.RepoPath, record.Branch, record.Vendor, record.Model, record.StartedUtc, record.UpdatedUtc)));
        var questions = new QuestionConsultStore(dataDir).All()
            .Where(record => record.Status == QuestionConsultStatuses.Consulting)
            .Select(record => (Touched: Touched(record.UpdatedUtc, record.StartedUtc), Row: Row(
                record.Id, "consask", record.Status, record.RepoPath, record.Branch, string.Empty, string.Empty, record.StartedUtc, record.UpdatedUtc)));

        return [.. consults.Concat(questions).OrderByDescending(one => one.Touched).Take(MostRows).Select(one => one.Row)];
    }

    private static DateTimeOffset Touched(string updated, string started) =>
        Instant(updated) ?? Instant(started) ?? DateTimeOffset.MinValue;

    private static JsonElement Row(string id, string kind, string status, string repo, string branch, string vendor, string model, string started, string updated)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("id", id);
            json.WriteString("kind", kind);
            json.WriteString("status", status);
            json.WriteString("repo", repo);
            json.WriteString("branch", branch);
            if (vendor.Length > 0)
            {
                json.WriteString("vendor", vendor);
            }

            if (model.Length > 0)
            {
                json.WriteString("model", model);
            }

            json.WriteString("startedUtc", started);
            json.WriteString("updatedUtc", updated);
            json.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.ToArray()).RootElement.Clone();
    }

    private static List<JsonElement> Failures(string dataDir, DateTimeOffset now)
    {
        var since = now - FailureWindow;
        var found = new List<(DateTimeOffset At, JsonElement Row)>();
        foreach (var line in TailLines(Path.Combine(dataDir, ServerNotices.Name)))
        {
            if (FailureOf(line, since) is { } failure)
            {
                found.Add(failure);
            }
        }

        return [.. found.OrderByDescending(one => one.At).Take(MostRows).Select(one => one.Row)];
    }

    /// <summary>The line's failure, or nothing: a torn line, another class, an older instant.</summary>
    private static (DateTimeOffset At, JsonElement Row)? FailureOf(string line, DateTimeOffset since)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "class") != "failure"
                || Instant(Text(root, "utc")) is not { } at
                || at < since)
            {
                return null;
            }

            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer, WriterOptions))
            {
                json.WriteStartObject();
                foreach (var field in new[] { "utc", "code", "source", "subject", "title", "detail", "cure", "repo" })
                {
                    if (Text(root, field) is { Length: > 0 } value)
                    {
                        json.WriteString(field, value);
                    }
                }

                json.WriteEndObject();
            }

            return (at, JsonDocument.Parse(buffer.ToArray()).RootElement.Clone());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static DateTimeOffset? Instant(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ? at : null;

    /// <summary>
    /// The whole lines in the last <see cref="TailBytes"/> of the file — none for a file that is not there. The file is opened
    /// for sharing, because the server appends to it while this reads; a first line cut by the window is dropped.
    /// </summary>
    private static IEnumerable<string> TailLines(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var from = Math.Max(0, stream.Length - TailBytes);
        stream.Seek(from, SeekOrigin.Begin);
        var bytes = new byte[stream.Length - from];
        var read = 0;
        while (read < bytes.Length)
        {
            var got = stream.Read(bytes, read, bytes.Length - read);
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        var lines = Encoding.UTF8.GetString(bytes, 0, read).Split('\n');

        return from > 0 ? lines.Skip(1).ToList() : lines.ToList();
    }
}
