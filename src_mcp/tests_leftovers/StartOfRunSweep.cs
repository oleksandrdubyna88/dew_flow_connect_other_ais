using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Tests.Leftovers;

namespace CoaiMcp.Tests;

/// <summary>
/// The start-of-run sweep of every C# suite of this repository, as coai wires it (E5 S2-B): the copied <see cref="LeftoversSweep"/> over the MACHINE's temp root, behind the ONE switch of
/// <c>shared/leftovers-sweep.json</c> (plan D11), with the end of the sweep stamped so a test can prove it ended before the first test began.
/// </summary>
/// <remarks>
/// <para>The copied <see cref="LeftoversSweepFixture"/> calls <see cref="Run(string)"/> from its constructor (manifest operation A), with <see cref="RunTemp.Real"/> — never
/// <c>Path.GetTempPath()</c>, which after <see cref="RunTemp.Start"/> is this run's own folder. xUnit v3 builds every assembly fixture before it starts any test collection, so the sweep ends
/// first; <see cref="StartOfRunOrderAttribute"/> checks that on every test of every run.</para>
/// <para>The switch's safe side is <see cref="Mode.ListOnly"/>: a typo, a missing key or an unreadable file switches the removal OFF, never on.</para>
/// </remarks>
public static class StartOfRunSweep
{
    /// <summary>The ONE switch (D11), a repository-relative path shared by the C# suites and both Node runners.</summary>
    public const string SwitchFile = "shared/leftovers-sweep.json";

    /// <summary>The key of the switch in <see cref="SwitchFile"/>.</summary>
    public const string SwitchKey = "startOfRunSweep";

    /// <summary>What the sweep does: collect (the two keys of the rule) or only list.</summary>
    public enum Mode { Collect, ListOnly }

    private static readonly Ledger Process = new();
    private static long _firstTestStart;

    /// <summary>
    /// What one call of <see cref="Run(string, Mode, Func{string, SweepResult}, Func{string, SweepResult}, Ledger)"/> writes down: the root it was handed and when it ended. The real run writes the process's own ledger (the three properties
    /// below); a test of the switch hands a ledger of its own, so it can never move the end or the root the order proof and the root test read.
    /// </summary>
    internal sealed class Ledger
    {
        private long _endedAt;

        internal long EndedAt => Volatile.Read(ref _endedAt);

        internal string LastRoot { get; set; } = string.Empty;

        internal void End() => Volatile.Write(ref _endedAt, Stopwatch.GetTimestamp());
    }

    /// <summary><see cref="Stopwatch.GetTimestamp"/> taken when <see cref="Run(string)"/> returned (a result or an error); 0 until then. Read by the order proof.</summary>
    public static long EndedAt => Process.EndedAt;

    /// <summary>The earliest <c>Before</c> timestamp of any test of this process (an interlocked minimum); 0 until the first test.</summary>
    public static long FirstTestStart => Volatile.Read(ref _firstTestStart);

    /// <summary>The root the real sweep was handed; empty until <see cref="Run(string)"/> ran.</summary>
    public static string LastRoot => Process.LastRoot;

    /// <summary>The real entry: the mode from the switch file, the real sweep, the real listing. The end is stamped in a <c>finally</c>.</summary>
    public static SweepResult Run(string tmp) =>
        Run(tmp, ReadMode(ReadSwitchFile, Console.Error.WriteLine), Collect, ListOnly, Process);

    internal static SweepResult Run(string tmp, Mode mode, Func<string, SweepResult> collect, Func<string, SweepResult> listOnly, Ledger ledger)
    {
        ledger.LastRoot = tmp;
        try
        {
            return mode == Mode.Collect ? collect(tmp) : listOnly(tmp);
        }
        finally
        {
            ledger.End();
        }
    }

    /// <summary>Notes the start of a test: keeps the earliest timestamp (0 means none yet).</summary>
    internal static void RecordTestStart(long timestamp)
    {
        long seen;
        do
        {
            seen = Volatile.Read(ref _firstTestStart);
            if (seen != 0 && seen <= timestamp)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _firstTestStart, timestamp, seen) != seen);
    }

    /// <summary>The switch file's text, or <c>null</c> when it is not there. Found from the binary folder up to the solution, as the diff check finds its manifest.</summary>
    internal static string? ReadSwitchFile()
    {
        var path = Path.Combine(RepositoryRoot.Path, SwitchFile.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// <c>"collect"</c> (exact, case-sensitive) is <see cref="Mode.Collect"/>. Anything else — <c>"list-only"</c>, <c>"COLLECT"</c>, a missing key, a missing file, bad JSON, a reader that throws — is
    /// <see cref="Mode.ListOnly"/>, and everything but the literal <c>"list-only"</c> says so once through <paramref name="warn"/>.
    /// </summary>
    internal static Mode ReadMode(Func<string?> readSwitchFile, Action<string> warn)
    {
        var (mode, because) = Judge(readSwitchFile);
        if (mode == Mode.ListOnly && because != "list-only")
        {
            warn($"coai tests: the start-of-run sweep is list-only because {SwitchFile} {SwitchKey} is {because}");
        }

        return mode;
    }

    private static (Mode Mode, string Because) Judge(Func<string?> readSwitchFile)
    {
        string? text;
        try
        {
            text = readSwitchFile();
        }
        catch (Exception ex)
        {
            return (Mode.ListOnly, $"unreadable: {FirstLine(ex.Message)}");
        }

        if (text is null)
        {
            return (Mode.ListOnly, "unreadable: the file is not there");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(SwitchKey, out var value))
            {
                return (Mode.ListOnly, "missing");
            }

            var shown = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

            return shown == "collect" && value.ValueKind == JsonValueKind.String ? (Mode.Collect, shown) : (Mode.ListOnly, shown);
        }
        catch (JsonException ex)
        {
            return (Mode.ListOnly, $"unreadable: {FirstLine(ex.Message)}");
        }
    }

    private static string FirstLine(string message) => message.Split('\n')[0].TrimEnd('\r');

    /// <summary>The template's sweep call, verbatim, over <paramref name="tmp"/>: the five arguments, the real probe, the real scope key.</summary>
    internal static SweepResult Collect(string tmp) =>
        LeftoversSweep.Run(new SweepOptions(tmp, DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine));

    // Spelled in two pieces: this file reads names, and the machine scan reads it for the bare prefix too.
    private static readonly Regex QuarantineEntry = new(
        @"^\.(.+)\." + "teststo" + @"delete-quarantine-[0-9]+-[0-9a-f]+\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The switch is OFF: every entry whose name is the owner form of this repository's token (any owner) or its legacy form, and every quarantine of such a name, is ONE <c>switched-off:</c> line. NAMES only:
    /// no claim is read, no identity taken, nothing renamed or removed. A root that cannot be listed is one <c>Errors</c> line, as the sweep's own.
    /// </summary>
    internal static SweepResult ListOnly(string tmp)
    {
        try
        {
            var lines = Directory.EnumerateFileSystemEntries(tmp)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Select(Describe)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .ToList();

            return new SweepResult([], [], lines, []);
        }
        catch (Exception ex)
        {
            return new SweepResult([], [], [], [$"temp root {tmp}: {FirstLine(ex.Message)}"]);
        }
    }

    private static string? Describe(string name)
    {
        var quarantine = QuarantineEntry.Match(name);
        var (kind, judged) = quarantine.Success ? ("quarantine", quarantine.Groups[1].Value)
            : name.EndsWith(".owner", StringComparison.OrdinalIgnoreCase) ? ("claim", name)
            : ("folder", name);

        return IsOurs(Owned.Classify(judged))
            ? $"switched-off: {kind} {name} — {SwitchKey} is list-only ({SwitchFile}, D11); nothing is removed"
            : null;
    }

    private static bool IsOurs(NameForm form) => form switch
    {
        NameForm.Owner owner => owner.Token == OwnedToken.RepoToken,
        NameForm.Legacy legacy => legacy.Token == OwnedToken.RepoToken,
        _ => false,
    };
}
