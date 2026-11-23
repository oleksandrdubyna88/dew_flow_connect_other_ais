using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Work;

/// <summary>
/// Writes the YAML of a story and of a task the way <c>v2/_gen/gen-v2.mjs</c> writes it — the subset <see cref="StrictYamlReader"/> reads — so a file the BA's output makes is a file a person (or the generator) could have written.
/// Pure: a value in, LF text out.
/// </summary>
/// <remarks>
/// <para><b>One quoting rule.</b> A scalar is plain when it is only <c>A-Z a-z 0-9 _ . / -</c> and is not one the reader would take for something else (<c>true</c>, <c>false</c>, <c>null</c>, <c>yes</c>, <c>no</c>, anything that starts with a
/// digit, the bare <c>-</c> that is a list marker); every other string is a double-quoted scalar with JSON escapes. The reader gives a quoted scalar the kind string, so a title that looks like a number or a word stays a string.
/// A test emits every task and story file of <c>v2/</c> from its read value and reads the text back: the nodes are equal (<c>TheYamlEmitterRoundTripsEveryV2FileTests</c>).</para>
/// <para>Text is LF with one final newline. The manifest of a BA run holds the SHA-256 of exactly this text, so a checkout that turns line ends into CRLF still compares equal.</para>
/// </remarks>
public static partial class WorkPlanYaml
{
    /// <summary>The comment line every file of <c>v2/</c> starts with.</summary>
    public const string Header = "# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)";

    [GeneratedRegex(@"^[A-Za-z0-9_./-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainScalar();

    [GeneratedRegex(@"^(true|false|null|yes|no|\d.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LooksLikeSomethingElse();

    /// <summary>The text of a <c>story.yaml</c>: id, title, <c>cornerstone</c> when the story says, <c>dependsOn</c>.</summary>
    public static string Story(BaStory story)
    {
        var text = new StringBuilder(Header).Append('\n');
        Line(text, "id", Scalar(story.Id));
        Line(text, "title", Scalar(story.Title));
        if (story.Cornerstone is { } cornerstone)
        {
            Line(text, "cornerstone", Bool(cornerstone));
        }

        Strings(text, "dependsOn", story.DependsOn);

        return text.ToString();
    }

    /// <summary>The text of a <c>task.yaml</c>: id, title, kind, complexity, dependsOn, parallel, <c>security</c> when the task says, files, dod.</summary>
    public static string Task(BaTask task)
    {
        var text = new StringBuilder(Header).Append('\n');
        Line(text, "id", Scalar(task.Id));
        Line(text, "title", Scalar(task.Title));
        Line(text, "kind", Scalar(task.Kind));
        Line(text, "complexity", task.Complexity.ToString(CultureInfo.InvariantCulture));
        Strings(text, "dependsOn", task.DependsOn);
        Line(text, "parallel", Bool(task.Parallel));
        if (task.Security is { } security)
        {
            Line(text, "security", Bool(security));
        }

        Strings(text, "files", task.Files);
        text.Append("dod:\n");
        foreach (var item in task.Dod)
        {
            text.Append("  - id: ").Append(Scalar(item.Id)).Append('\n');
            text.Append("    item: ").Append(Scalar(item.Item)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>A string as one scalar: plain when that is safe, else double-quoted with JSON escapes.</summary>
    public static string Scalar(string value) =>
        PlainScalar().IsMatch(value) && value != "-" && !LooksLikeSomethingElse().IsMatch(value) ? value : Quoted(value);

    private static string Bool(bool value) => value ? "true" : "false";

    private static void Line(StringBuilder text, string key, string value) => text.Append(key).Append(": ").Append(value).Append('\n');

    private static void Strings(StringBuilder text, string key, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            Line(text, key, "[]");

            return;
        }

        text.Append(key).Append(":\n");
        foreach (var value in values)
        {
            text.Append("  - ").Append(Scalar(value)).Append('\n');
        }
    }

    /// <summary>What <c>JSON.stringify</c> writes: the quote, the backslash and every control character escaped, everything else as it is.</summary>
    private static string Quoted(string value)
    {
        var text = new StringBuilder("\"");
        foreach (var c in value)
        {
            text.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }

        return text.Append('"').ToString();
    }
}
