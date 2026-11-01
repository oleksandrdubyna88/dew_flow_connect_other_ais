using System.Text.RegularExpressions;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// codex's own <c>config.toml</c>, as a launch's environment locates it — the one reader of that file in this library.
/// </summary>
/// <remarks>
/// <para>Extracted from <see cref="NoMcpServers"/> (issue #514), which needed the file's MCP server names, when story 3.4 of
/// research/PLAN_every_round_is_counted.md needed two of its root keys: <c>model_reasoning_effort</c> and <c>service_tier</c>,
/// read to LABEL a ledger line whose launch sent neither. One locator, so "which config.toml does this launch read" has
/// one answer — on a Team server the SLOT's, never the server's (our own code reviewer, #514).</para>
/// <para><b>Not a TOML parser, and unsure means silent.</b> A value is read only from a root-level <c>key = value</c>
/// line before the first table header, as a basic string, a literal string or a bare word. A key that is only set inside
/// a profile, or spelled in a form this does not read, labels nothing — an unlabelled line is the truth about what is
/// known; a wrongly labelled one is a measurement that lies.</para>
/// </remarks>
public static partial class CodexConfigFile
{
    /// <summary>How long one match may run: the input is a local config file, but a pattern is never unbounded.</summary>
    private const int MatchTimeoutMs = 1000;

    /// <summary><c>key = "basic"</c>, <c>key = 'literal'</c> or <c>key = bare</c>, with an optional trailing comment.</summary>
    [GeneratedRegex("""^(?<key>[A-Za-z0-9_-]+)\s*=\s*(?:"(?<basic>[^"\\]*)"|'(?<literal>[^']*)'|(?<bare>[A-Za-z0-9_.-]+))\s*(?:#.*)?$""",
        RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex RootKey { get; }

    /// <summary>The text of <c>$CODEX_HOME/config.toml</c> — else <c>~/.codex/config.toml</c> — or empty when it cannot be read.</summary>
    /// <param name="env">The environment the LAUNCH will have: on a Team server, the slot's own <c>CODEX_HOME</c> and <c>HOME</c>.</param>
    public static string TextIn(Func<string, string?> env)
    {
        var path = Path.Combine(
            env("CODEX_HOME") is { Length: > 0 } codexHome ? codexHome : Path.Combine(HomeIn(env), ".codex"), "config.toml");
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>A ROOT key's value — set before the first table header — or empty when the file does not set it plainly.</summary>
    public static string RootValue(string toml, string key)
    {
        foreach (var raw in toml.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                return string.Empty;
            }

            if (RootKey.Match(line) is { Success: true } found && found.Groups["key"].Value == key)
            {
                return ValueOf(found);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// A key's value inside ONE named table — the lines after the FIRST line that trims to exactly <paramref name="header"/>
    /// (<c>[providers."managed:kimi-code".oauth]</c>) up to the next line that starts with <c>[</c> — read as
    /// <see cref="RootValue"/> reads a root key; empty when the table or the key is not there or not spelled plainly.
    /// </summary>
    /// <remarks>
    /// For Kimi's config (D19): the same "unsure means silent", with the same one reader. The text is sliced to the table BEFORE
    /// any key is looked for, so the same name in another table (<c>base_url</c> is in the managed provider and the
    /// <c>moonshot-ai</c> one) is never confused, and a malformed line outside the table is never parsed.
    /// </remarks>
    public static string TableValue(string toml, string header, string key)
    {
        foreach (var line in TableLines(toml, header))
        {
            if (RootKey.Match(line) is { Success: true } found && found.Groups["key"].Value == key)
            {
                return ValueOf(found);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// A ONE-LINE array of basic strings, <c>key = [ "a", "b" ]</c>, inside the named table; empty when it is absent or of any
    /// other form (a multi-line array, a non-string item, a literal string).
    /// </summary>
    public static IReadOnlyList<string> TableStrings(string toml, string header, string key)
    {
        foreach (var line in TableLines(toml, header))
        {
            if (ArrayKey.Match(line) is { Success: true } found && found.Groups["key"].Value == key)
            {
                return StringsIn(found.Groups["items"].Value);
            }
        }

        return [];
    }

    /// <summary>Whether a line trims to exactly <paramref name="header"/>.</summary>
    public static bool HasTable(string toml, string header) => toml.Split('\n').Any(line => line.Trim() == header);

    /// <summary>The trimmed lines of the table whose first header is <paramref name="header"/>, up to the next line that starts with <c>[</c>.</summary>
    private static IEnumerable<string> TableLines(string toml, string header)
    {
        var inside = false;
        foreach (var raw in toml.Split('\n'))
        {
            var line = raw.Trim();
            if (inside && line.StartsWith('['))
            {
                yield break;
            }

            if (inside)
            {
                yield return line;
            }

            inside |= line == header;
        }
    }

    /// <summary>The strings of an array body, or none when anything but strings and commas is in it.</summary>
    private static IReadOnlyList<string> StringsIn(string items)
    {
        var strings = BasicString.Matches(items).Select(match => match.Groups[1].Value).ToList();

        return BasicString.Replace(items, string.Empty).All(character => character == ',' || char.IsWhiteSpace(character)) ? strings : [];
    }

    /// <summary><c>key = [ ... ]</c> on ONE line, with an optional trailing comment; the body holds no bracket.</summary>
    [GeneratedRegex("""^(?<key>[A-Za-z0-9_-]+)\s*=\s*\[(?<items>[^\[\]]*)\]\s*(?:#.*)?$""", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex ArrayKey { get; }

    /// <summary>One basic string.</summary>
    [GeneratedRegex("""
        "([^"\\]*)"
        """, RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex BasicString { get; }

    private static string ValueOf(Match found) =>
        found.Groups["basic"].Success ? found.Groups["basic"].Value
        : found.Groups["literal"].Success ? found.Groups["literal"].Value
        : found.Groups["bare"].Value;

    /// <summary>The home directory the given environment names — <c>HOME</c>, then <c>USERPROFILE</c> — else this process's.</summary>
    private static string HomeIn(Func<string, string?> env)
    {
        if (env("HOME") is { Length: > 0 } unix)
        {
            return unix;
        }

        return env("USERPROFILE") is { Length: > 0 } windows ? windows : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
