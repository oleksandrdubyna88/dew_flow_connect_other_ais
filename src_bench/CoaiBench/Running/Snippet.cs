using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiBench.Running;

/// <summary>
/// Which version of the CLAUDE.md snippet the repository under test is carrying.
/// </summary>
/// <remarks>
/// <para>Part of the environment, not a detail. The snippet is how a target repository's AI learns
/// that the gate can hand back ORDERS and that they outrank its own habits — so a checkout carrying
/// v4 and one carrying v5 are two different machines, and a campaign that does not say which it
/// measured cannot be compared with the next one.</para>
/// <para>Read from the marker the extension writes, which is the same string its own reader looks
/// for: <c>&lt;!-- coai-snippet v5 --&gt;</c>.</para>
/// </remarks>
public static partial class Snippet
{
    [GeneratedRegex(@"<!-- coai-snippet v(\d+) -->")]
    private static partial Regex Marker { get; }

    /// <summary>The files a snippet is ever pasted into, in the order the extension looks.</summary>
    private static readonly string[] Candidates = ["CLAUDE.md", "AGENTS.md", ".claude/CLAUDE.md"];

    /// <summary>
    /// The version in this checkout, or a sentence saying there is none.
    /// </summary>
    /// <remarks>
    /// "none" is a real answer and is reported as one: a repository that never adopted the gate is
    /// not a broken repository, but a campaign run in it measures an assistant that was never told
    /// the orders exist.
    /// </remarks>
    public static string VersionIn(string repo)
    {
        foreach (var candidate in Candidates)
        {
            var file = Path.Combine(repo, candidate);
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                var found = Marker.Match(File.ReadAllText(file));
                if (found.Success)
                {
                    return $"v{found.Groups[1].Value} ({candidate})";
                }

                return $"none — {candidate} carries no snippet marker";
            }
            catch (IOException)
            {
                return $"unreadable — {candidate}";
            }
        }

        return "none — no CLAUDE.md in this checkout";
    }

    /// <summary>The number alone, for a table. Zero when there is no snippet.</summary>
    public static int NumberIn(string repo)
    {
        var version = VersionIn(repo);
        var digits = Marker.Match($"<!-- coai-snippet {version.Split(' ')[0]} -->");

        return digits.Success
            ? int.Parse(digits.Groups[1].Value, CultureInfo.InvariantCulture)
            : 0;
    }
}
