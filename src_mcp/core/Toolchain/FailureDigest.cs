using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CoaiMcp.Core.Notices;

namespace CoaiMcp.Core.Toolchain;

/// <summary>One failure of a step, as the digest sends it to the coder session.</summary>
/// <param name="Step">"build" or "test".</param>
/// <param name="File">The file the failure is in, relative to the tree when the digest was told the tree; empty when the output names none.</param>
/// <param name="Line">The line in <paramref name="File"/>; 0 when there is none.</param>
/// <param name="Code">The compiler's code (<c>CS0103</c>) for a build error; the test's full name for a failed test; empty for the raw fallback.</param>
/// <param name="Message">What the output says, redacted of credential-like text and cut.</param>
public sealed record DigestItem(string Step, string File, int Line, string Code, string Message)
{
    /// <summary>The fingerprint of the failure (E4 s4 t4; <see cref="DigestFingerprint"/>): the step, the file, the code and the message with digit runs and white space made equal, without the line number. Computed, never sent to the coder.</summary>
    public string Fingerprint => DigestFingerprint.Of(this);
}

/// <summary>Which reader understood the output.</summary>
public enum DigestKind
{
    /// <summary>MSBuild error lines (<c>file(line,col): error CS0103: message [project]</c>), and the errors of a failed <c>dotnet restore</c> (<c>NU1101</c>).</summary>
    MsBuild,

    /// <summary>The failed tests the test executable printed (the Microsoft Testing Platform form).</summary>
    TestExecutable,

    /// <summary>The VSTest console of <c>dotnet test</c>.</summary>
    VsTest,

    /// <summary>The error lines of <c>tsc</c>.</summary>
    Tsc,

    /// <summary>The error block of a failed <c>npm</c> command.</summary>
    Npm,

    /// <summary>The console of jest.</summary>
    Jest,

    /// <summary>The console of vitest.</summary>
    Vitest,

    /// <summary>The console of <c>node --test</c>.</summary>
    NodeTest,

    /// <summary>No reader understood the output: the last lines of it.</summary>
    RawTail,
}

/// <summary>The digest of a failed step.</summary>
/// <param name="Items">At most <see cref="FailureDigest.MaxItems"/>, and together at most <see cref="FailureDigest.MaxBytes"/> as JSON.</param>
/// <param name="Found">How many failures the output held — more than <paramref name="Items"/> when the digest was cut.</param>
/// <param name="Cut">Items were left out to keep the digest within its limits.</param>
/// <param name="Kind">Which reader produced the items.</param>
public sealed record FailureDigestResult(IReadOnlyList<DigestItem> Items, int Found, bool Cut, DigestKind Kind)
{
    /// <summary>The run summary of a failed TEST step (E4 s4 t5): the tool's counts, the name of every failure and whether the list is complete; null for any other step and when no test reader understood the output. The re-run of section F reads it, never the digest.</summary>
    public RunSummary? Summary { get; init; }
}

/// <summary>
/// What a failed step goes back to the coder session as: <c>[{step, file, line, code, message}]</c> from the step's own machine-readable output (server.md § 10) — the MSBuild error lines of a failed
/// <c>dotnet</c> build and the failed tests the test executable prints — and the last lines of the raw output only when no reader knows it.
/// </summary>
/// <remarks>
/// <para><b>Never more than <see cref="MaxItems"/> items or <see cref="MaxBytes"/> bytes</b> as JSON; the first failures are kept in the order the output had them, and the result says how many there were and that it
/// was cut. <b>Never empty</b>: a failed step whose output no reader understands is one item with the tail of it, or one saying the step wrote nothing — a coder told nothing cannot fix anything.</para>
/// <para><b>Credential-like text is redacted</b> by <see cref="Redaction"/> — the same passes as every other text this product writes down — before anything is cut, so a cut cannot leave half a secret.</para>
/// <para><b>Readers are chosen by the adapter and the step</b>, not by what the output looks like: an MSBuild-shaped line in a <c>node</c> project's build is raw text. Pure: text in, a value out.</para>
/// </remarks>
public static class FailureDigest
{
    public const int MaxItems = 40;

    public const int MaxBytes = 16 * 1024;

    /// <summary>The lines of raw output kept when no reader knows the output.</summary>
    internal const int TailLines = 30;

    /// <summary>The characters of raw output kept: the end of the last <see cref="TailLines"/> lines.</summary>
    internal const int TailChars = 8000;

    /// <summary>Longest message of one item, in characters.</summary>
    internal const int MessageChars = 1500;

    /// <summary>Longest file name or code of one item, in characters.</summary>
    internal const int FieldChars = 300;

    /// <summary>A line longer than this is not an error line of any reader: skipped, so one huge line cannot cost a search.</summary>
    internal const int LongestLine = 4096;

    /// <summary>The digest of <paramref name="output"/>, the log of a step that did not pass. <paramref name="treeRoot"/>, when given, is cut from the front of file names.</summary>
    /// <param name="runner">The runner fact of the project (<c>mtp</c>, <c>vstest</c>, <c>jest</c>, <c>vitest</c>, <c>node-test</c>), or empty: a step of the toolchain file has none, and the adapter's readers are tried in one fixed order, the first that gives an item winning.</param>
    /// <param name="logCut">The step's log was cut at its limit: a run summary is then never complete.</param>
    public static FailureDigestResult Of(CheckStep step, string adapter, string output, string treeRoot = "", string runner = "", bool logCut = false)
    {
        var name = step.Wire();
        var text = output.ReplaceLineEndings("\n");
        var (kind, found, summary) = Read(step, adapter, runner, name, text, logCut);

        return Bounded(kind, [.. found.Select(item => item with { File = Relative(item.File, treeRoot) })]) with { Summary = summary };
    }

    /// <summary>One reader of the family: what it is called in a digest, what it reads, and its run summary when it is a test reader.</summary>
    private sealed record Reader(DigestKind Kind, string Runner, Func<string, string, List<DigestItem>> Read, Func<string, bool, RunSummary?>? Summarize);

    private static readonly Reader MsBuild = new(DigestKind.MsBuild, "", MsBuildLines.Read, null);

    private static readonly Reader Tsc = new(DigestKind.Tsc, "", TscLines.Read, null);

    private static readonly Reader Npm = new(DigestKind.Npm, "", NpmErrors.Read, null);

    private static readonly Reader Mtp = new(DigestKind.TestExecutable, "mtp", TestExecutableFailures.Read, TestExecutableFailures.Summarize);

    private static readonly Reader VsTest = new(DigestKind.VsTest, "vstest", DotnetTestFailures.Read, DotnetTestFailures.Summarize);

    private static readonly Reader Jest = new(DigestKind.Jest, "jest", JestFailures.Read, JestFailures.Summarize);

    private static readonly Reader Vitest = new(DigestKind.Vitest, "vitest", VitestFailures.Read, VitestFailures.Summarize);

    private static readonly Reader NodeTest = new(DigestKind.NodeTest, "node-test", NodeTestFailures.Read, NodeTestFailures.Summarize);

    /// <summary>The readers of a step in the order they are tried: the runner fact puts its own reader first; a step of the file has no runner fact, so the adapter's readers run in one fixed order.</summary>
    private static IEnumerable<Reader> Readers(CheckStep step, string adapter, string runner) => (adapter, step) switch
    {
        ("dotnet", CheckStep.Test) => Ordered(runner, [VsTest, Mtp, MsBuild]),
        ("dotnet", CheckStep.Restore or CheckStep.Build) => [MsBuild],
        ("node", CheckStep.Test) => Ordered(runner, [Jest, Vitest, NodeTest]),
        ("node", CheckStep.Build or CheckStep.Typecheck) => [Tsc, Npm],
        ("node", CheckStep.Restore) => [Npm],
        _ => [],
    };

    private static IEnumerable<Reader> Ordered(string runner, Reader[] all) => all.Where(reader => reader.Runner == runner).Concat(all.Where(reader => reader.Runner != runner));

    private static (DigestKind Kind, IReadOnlyList<DigestItem> Items, RunSummary? Summary) Read(CheckStep step, string adapter, string runner, string name, string text, bool logCut)
    {
        foreach (var reader in Readers(step, adapter, runner))
        {
            if (reader.Read(name, text) is { Count: > 0 } items)
            {
                return (reader.Kind, items, step == CheckStep.Test ? reader.Summarize?.Invoke(text, logCut) : null);
            }
        }

        return (DigestKind.RawTail, [RawTail(name, text)], step == CheckStep.Test ? Readers(step, adapter, runner).Select(reader => reader.Summarize?.Invoke(text, logCut)).FirstOrDefault(summary => summary is not null) : null);
    }

    /// <summary>The items cut to <see cref="MaxItems"/>, then to <see cref="MaxBytes"/>: whole items from the end first, and for one item that alone is too big, its message.</summary>
    private static FailureDigestResult Bounded(DigestKind kind, List<DigestItem> all)
    {
        var items = all.Take(MaxItems).ToList();
        while (items.Count > 1 && Bytes(items) > MaxBytes)
        {
            items.RemoveAt(items.Count - 1);
        }

        while (Bytes(items) > MaxBytes && items[0].Message.Length > 0)
        {
            items[0] = items[0] with { Message = items[0].Message[((items[0].Message.Length + 1) / 2)..] };
        }

        return new FailureDigestResult(items, all.Count, items.Count < all.Count, kind);
    }

    private static int Bytes(List<DigestItem> items) => Encoding.UTF8.GetByteCount(Json(items));

    /// <summary>A file name as the tree sees it: <c>/</c> separators, the tree's own root cut off the front.</summary>
    private static string Relative(string file, string treeRoot)
    {
        var path = file.Replace('\\', '/');
        var root = treeRoot.Replace('\\', '/').TrimEnd('/');

        return root.Length > 0 && path.StartsWith(root + "/", StringComparison.Ordinal) ? path[(root.Length + 1)..] : path;
    }

    /// <summary>One item with the end of the output: the last <see cref="TailLines"/> lines, redacted whole and then cut to their last <see cref="TailChars"/> characters.</summary>
    private static DigestItem RawTail(string step, string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var tail = Redaction.SafeSource(string.Join('\n', lines.TakeLast(TailLines)));

        return new DigestItem(step, string.Empty, 0, string.Empty, tail.Length == 0 ? "the step wrote no output" : DigestText.Tail(tail, TailChars));
    }

    /// <summary>The digest as the coder session is sent it: a JSON array of <c>{step, file, line, code, message}</c>, in that key order.</summary>
    public static string Json(IReadOnlyList<DigestItem> items)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("step", item.Step);
                writer.WriteString("file", item.File);
                writer.WriteNumber("line", item.Line);
                writer.WriteString("code", item.Code);
                writer.WriteString("message", item.Message);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>The two ways a text of a digest is shortened: the head of it, or the end.</summary>
internal static class DigestText
{
    private const string Ellipsis = "…";

    /// <summary>Redacted, then at most <paramref name="max"/> characters from the front — never ending inside a surrogate pair.</summary>
    internal static string Head(string text, int max)
    {
        var safe = Redaction.SafeSource(text);
        if (safe.Length <= max)
        {
            return safe;
        }

        var length = char.IsHighSurrogate(safe[max - 1]) ? max - 1 : max;

        return safe[..length] + Ellipsis;
    }

    /// <summary>A line number from digits; 0 when it does not fit — a number of any length can be written, and a digest never throws for one.</summary>
    internal static int Number(string digits) => int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : 0;

    /// <summary>At most <paramref name="max"/> characters from the END of an already redacted text, with a mark that the front was cut.</summary>
    internal static string Tail(string safe, int max)
    {
        if (safe.Length <= max)
        {
            return safe;
        }

        var from = safe.Length - max;
        if (char.IsLowSurrogate(safe[from]))
        {
            from++;
        }

        return Ellipsis + safe[from..];
    }
}
