using System.Text.RegularExpressions;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Notices;

namespace CoaiMcp.Core.Security;

public sealed record SecurityFile(FileDiff Diff, IReadOnlyList<string> Signals)
{
    public bool DetectionIncomplete { get; init; }
    public bool SupportingMaterial => SecuritySignals.IsSupportingMaterial(Diff.Path);
}

/// <summary>Bounded lexical routing, including removed checks. These matches are not vulnerabilities.</summary>
public static class SecuritySignals
{
    public const int MaxFiles = 512;
    public const int MaxFileCharacters = 262144;
    private static readonly string[] SupportingFolders = ["docs", "research", "todo", "test", "tests", "__tests__", "fixtures"];

    // A ranking hint, never a reason to withhold code or declare a path safe.
    public static bool IsSupportingMaterial(string path) =>
        IsProse(path)
        || path.Replace('\\', '/').Split('/').Any(part => SupportingFolders.Contains(part, StringComparer.OrdinalIgnoreCase));

    // Prose remains available as supporting context, but cannot alone assert an application surface.
    private static bool IsProse(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" or ".rst";

    /// <summary>The files classified by the shipped signals — what the lane detects when the setting gives no words.</summary>
    public static IReadOnlyList<SecurityFile> Classify(IReadOnlyList<FileDiff> files) => Classify(files, SignalTable.Shipped);

    /// <summary>The files classified by <paramref name="table"/>: a person's words, a card's own words (PLAN_one_model_catalog.md E2.4).</summary>
    public static IReadOnlyList<SecurityFile> Classify(IReadOnlyList<FileDiff> files, SignalTable table) =>
        [.. files.Take(MaxFiles).OrderBy(f => f.Path, StringComparer.Ordinal).Select(file => Classify(file, table))];

    private static SecurityFile Classify(FileDiff file, SignalTable table)
    {
        var withheld = IsWithheld(file);
        var safe = withheld ? string.Empty : Redaction.SafeSource(WithinLimit(file.Text));
        var (signals, timedOut) = Detected(table, Detectable(file, safe));

        return new(file with { Text = safe }, signals) { DetectionIncomplete = Incomplete(file, withheld, timedOut) };
    }

    /// <summary>What a detector reads: the path and the redacted text — nothing for prose, which cannot assert an application surface.</summary>
    private static string Detectable(FileDiff file, string safe) => IsProse(file.Path) ? string.Empty : file.Path + "\n" + safe;

    /// <summary>A file the detector read but could not finish: too large, or a pattern that ran out of time on it.</summary>
    private static bool Incomplete(FileDiff file, bool withheld, bool timedOut) => !withheld && (IsOversized(file) || timedOut);

    /// <summary>
    /// The signals the text carries — and whether a pattern ran out of time on it, which leaves the file's detection
    /// INCOMPLETE (the lane's own word for a detector that could not finish), never quietly unmatched.
    /// </summary>
    private static (IReadOnlyList<string> Signals, bool TimedOut) Detected(SignalTable table, string text)
    {
        var found = new List<string>();
        var timedOut = false;
        foreach (var (signal, matcher) in table.Signals)
        {
            try
            {
                if (matcher.Matches(text)) found.Add(signal);
            }
            catch (RegexMatchTimeoutException)
            {
                timedOut = true;
            }
        }

        return (found, timedOut);
    }

    /// <summary>Binary content and credential files never reach a detector or a prompt.</summary>
    private static bool IsWithheld(FileDiff file) => file.IsBinary || CredentialFiles.LooksLikeOne(file.Path);

    private static bool IsOversized(FileDiff file) => file.Text.Length > MaxFileCharacters;

    private static string WithinLimit(string text) => text.Length <= MaxFileCharacters ? text : string.Empty;

    /// <summary>
    /// The one order a slice reads files in: production and configuration ahead of documentation and
    /// tests, then by how many of the focus signals a file carries. Shared by the sixteen-file source
    /// reader and the context composer, so the files given source are the ones the pack puts first.
    /// </summary>
    public static IEnumerable<SecurityFile> Rank(IEnumerable<SecurityFile> files, IEnumerable<string> focus)
    {
        var wanted = focus.ToHashSet(StringComparer.Ordinal);
        return files.OrderBy(f => f.SupportingMaterial).ThenByDescending(f => f.Signals.Count(wanted.Contains));
    }

    /// <summary>
    /// Whether a prompt is due for these files. An "always" prompt is due on every change with readable CODE — the
    /// operator's "on all code" — and not on prose alone, which cannot assert an application surface. Any other
    /// prompt with no triggers is due only when it is custom: an empty CONDITIONAL preset never becomes unconditional.
    /// </summary>
    public static bool Triggered(SecurityPrompt prompt, IReadOnlyList<SecurityFile> files) =>
        SecurityCatalog.IsAlways(prompt.Id) ? files.Any(IsReadableCode) : ByTriggers(prompt, files);

    /// <summary>
    /// Code a reviewer can be given: not prose, and not withheld (binary, credential file) — an oversized diff still
    /// counts, because it is code the detector could not finish. Decided by path and content, never by
    /// <see cref="IsSupportingMaterial"/>: test, docs and research folders hold code too, and that hint only ranks.
    /// </summary>
    public static bool IsReadableCode(SecurityFile file) =>
        !IsProse(file.Diff.Path) && (file.Diff.Text.Length > 0 || file.DetectionIncomplete);

    private static bool ByTriggers(SecurityPrompt prompt, IReadOnlyList<SecurityFile> files) =>
        prompt.Words.Count > 0 ? files.Any(f => f.Signals.Contains(prompt.OwnSignal) || f.Signals.Intersect(prompt.Triggers).Any())
        : prompt.Triggers.Count == 0 ? !SecurityCatalog.IsPreset(prompt.Id)
        : files.Any(f => f.Signals.Intersect(prompt.Triggers).Any());
}
