using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoaiMcp.Server.Migrations;

/// <summary>One line of <c>migrations/v2.journal</c>.</summary>
/// <param name="Kind"><c>session</c>, <c>card</c>, <c>store</c> or <c>settings</c>.</param>
/// <param name="State"><c>planned</c>, <c>done</c>, <c>skipped</c>, <c>deferred</c> or <c>unreadable</c>.</param>
internal sealed record MigrationJournalLine(int Seq, string Utc, string Kind, string Id, string State, string Detail);

/// <summary>The journal's lines are one JSON object each, so this context is compact — the server's own one indents.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(MigrationJournalLine))]
internal sealed partial class MigrationLineContext : JsonSerializerContext;

/// <summary>
/// <c>migrations/v2.journal</c>: JSON Lines, append-only, flushed to disk per line.
/// </summary>
/// <remarks>
/// <para><b>Evidence, not authority.</b> The files are the truth: every object's step is decided from the object itself, so a journal that is
/// lost, torn or wrong changes nothing but one thing — a session that carries the conversion note and has no <c>done</c> line gets its database
/// projection written again (<see cref="MigrationV2"/>).</para>
/// <para>A line torn by a kill is ignored on read, and the next append first cuts the file back to its last newline (<see cref="CutTornTail"/>), so a torn line is neither read nor kept and never spoils the one after it.</para>
/// </remarks>
internal sealed class MigrationV2Journal
{
    private readonly string _path;
    private readonly TimeProvider _clock;
    private int _seq;

    public MigrationV2Journal(string path, TimeProvider clock)
    {
        _path = path;
        _clock = clock;
        _seq = Read(path).Select(line => line.Seq).DefaultIfEmpty(0).Max();
    }

    /// <summary>Appends one line and flushes it to disk before returning.</summary>
    public void Append(string kind, string id, string state, string detail)
    {
        _seq++;
        var line = new MigrationJournalLine(_seq, _clock.GetUtcNow().UtcDateTime.ToString("O"), kind, id, state, detail);
        var text = JsonSerializer.Serialize(line, MigrationLineContext.Default.MigrationJournalLine) + "\n";
        using var file = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        CutTornTail(file);
        file.Seek(0, SeekOrigin.End);
        file.Write(System.Text.Encoding.UTF8.GetBytes(text));
        file.Flush(flushToDisk: true);
    }

    /// <summary>
    /// A journal whose last byte is not a newline ends in a line a kill cut short: the file is cut back to its last newline, so the torn bytes are gone before the next
    /// line is written and every line left in the file parses (SPEC §5.5). A journal that ends in a newline, or is empty, is left as it is.
    /// </summary>
    private static void CutTornTail(FileStream file)
    {
        if (file.Length == 0)
        {
            return;
        }

        file.Seek(-1, SeekOrigin.End);
        if (file.ReadByte() == '\n')
        {
            return;
        }

        file.SetLength(LastNewline(file) + 1);
    }

    /// <summary>The offset of the last newline in the file, or -1 when there is none — read backwards in blocks, the torn line being short.</summary>
    private static long LastNewline(FileStream file)
    {
        var block = new byte[4096];
        var end = file.Length;
        while (end > 0)
        {
            var start = Math.Max(0, end - block.Length);
            var count = (int)(end - start);
            file.Seek(start, SeekOrigin.Begin);
            file.ReadExactly(block, 0, count);
            var at = Array.LastIndexOf(block, (byte)'\n', count - 1);
            if (at >= 0)
            {
                return start + at;
            }

            end = start;
        }

        return -1;
    }

    /// <summary>Every line that parses, in file order. A missing file is no lines; a line that does not parse (a torn one) is skipped.</summary>
    public static IReadOnlyList<MigrationJournalLine> Read(string path)
    {
        string text;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file, System.Text.Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }

        var lines = new List<MigrationJournalLine>();
        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (JsonSerializer.Deserialize(raw, MigrationLineContext.Default.MigrationJournalLine) is { } line)
                {
                    lines.Add(line);
                }
            }
            catch (JsonException)
            {
                // a torn line: evidence that a write was cut short, nothing more
            }
        }

        return lines;
    }
}
