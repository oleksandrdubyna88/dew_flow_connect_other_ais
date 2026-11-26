using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// <b>The one function that spells a name of the head tree in a sentence</b> (epoch 3 E4, M3 of the s2 security verify). A path of a repository is written by the model whose work is being checked, and a refusal, a note or a digest may reach a model or a person: a name is spelled only when it matches a closed pattern
/// of ordinary characters and a bound; anything else — a line separator, a bidirectional override, a zero-width or format character, a long name — is replaced by a sentence that says there is one and how long it is. A VALUE of a manifest (an <c>Include</c>, a script, a parser's message) is never spelled at all: the sentences state counts and positions.
/// </summary>
public static partial class PathSpelling
{
    /// <summary>The longest name spelled in full.</summary>
    public const int MaxLength = 200;

    /// <summary>How many items of a list a sentence names before it counts the rest.</summary>
    public const int MaxListed = 5;

    [GeneratedRegex("^[A-Za-z0-9._/@+ -]{1,200}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Safe();

    /// <summary>True for a name the closed pattern lets through.</summary>
    public static bool IsSafe(string path) => path.Length <= MaxLength && Safe().IsMatch(path);

    /// <summary>The path itself when it is safe (no quotes: for a label inside a sentence), else the fixed phrase of <see cref="Spell"/>.</summary>
    public static string Bare(string path, string oid = "") => IsSafe(path) ? path : Phrase(path, oid);

    private static string Phrase(string path, string oid) =>
        $"a path with unusual characters ({path.Length.ToString(CultureInfo.InvariantCulture)} characters{(oid.Length >= 12 ? ", blob " + oid[..12] : string.Empty)})";

    /// <summary>The path in single quotes when it is safe, else a fixed phrase that names its length (and, when known, the first twelve characters of its object id).</summary>
    public static string Spell(string path, string oid = "") =>
        IsSafe(path) ? $"'{path}'" : Phrase(path, oid);

    /// <summary>A list of paths for a sentence: at most <see cref="MaxListed"/> spelled, then the count of the rest.</summary>
    public static string SpellMany(IEnumerable<string> paths, string separator = ", ")
    {
        var all = paths.ToList();
        var listed = string.Join(separator, all.Take(MaxListed).Select(path => Spell(path)));

        return all.Count > MaxListed ? $"{listed} and {(all.Count - MaxListed).ToString(CultureInfo.InvariantCulture)} more" : listed;
    }

    /// <summary>The comma-joined paths of a fact, for a sentence that lists what differs: each spelled, capped, never the raw text.</summary>
    public static string SpellFact(string joined) =>
        joined.Length == 0 ? "nothing" : SpellMany(joined.Split(',', StringSplitOptions.RemoveEmptyEntries));
}
