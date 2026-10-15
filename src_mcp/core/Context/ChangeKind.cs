using System.Collections.Immutable;
using CoaiMcp.Core.Outlining;

namespace CoaiMcp.Core.Context;

/// <summary>
/// What a changed file is FOR — one of five words, decided on its NEW path alone (E2.S1 §S1.3, parent §7).
/// </summary>
/// <remarks>
/// <para>First matching row of an ordered table wins: <c>test</c>, <c>docs</c>, <c>config</c>, <c>code</c>, then
/// <c>unknown</c>. A "path segment" is one WHOLE directory component at any depth (<c>contest/</c> is not
/// <c>test/</c>); the name rules apply to the last component only. Every comparison ignores case — this is the
/// product's own classification with no git twin, unlike the exclusions, which must agree with git byte for byte.</para>
/// <para><c>code</c> is whatever <see cref="OutlineLanguages.Of"/> maps and nothing else, so a language the outliner
/// learns is a kind this function learns; there is no second extension list.</para>
/// </remarks>
public static class ChangeKind
{
    public const string Code = "code";
    public const string Test = "test";
    public const string Docs = "docs";
    public const string Config = "config";
    public const string Unknown = "unknown";

    /// <summary>The order cells are written and compared in — the report's default denominator first.</summary>
    public static readonly ImmutableArray<string> All = [Code, Test, Docs, Config, Unknown];

    private static readonly string[] TestSegments = ["test", "tests", "__tests__"];
    private static readonly string[] DocsSegments = ["research", "todo", "docs"];
    private static readonly string[] TestInfixes = [".test.", ".spec."];
    private static readonly string[] DocsSuffixes = [".md", ".txt"];
    private static readonly string[] ConfigSuffixes = [".json", ".yml", ".yaml", ".toml", ".props", ".targets", ".csproj", ".slnx", ".config"];

    /// <summary>The rules, in the order they are tried; the last one always holds.</summary>
    private static readonly (string Kind, Func<string[], string, bool> Holds)[] Rules =
    [
        (Test, (dirs, name) => InSegment(dirs, TestSegments) || IsTestName(name)),
        (Docs, (dirs, name) => InSegment(dirs, DocsSegments) || EndsWithAny(name, DocsSuffixes)),
        (Config, (_, name) => EndsWithAny(name, ConfigSuffixes) || IsDotfile(name)),
        (Code, (_, name) => OutlineLanguages.Of(name) != OutlineLanguage.Unsupported),
        (Unknown, (_, _) => true),
    ];

    /// <summary>The kind of <paramref name="path"/> as <c>git diff --numstat -z</c> prints it; never throws.</summary>
    public static string Of(string path)
    {
        var parts = path.Split('/');

        return Rules.First(rule => rule.Holds(parts[..^1], parts[^1])).Kind;
    }

    private static bool InSegment(string[] dirs, string[] words) => dirs.Any(d => words.Contains(d, StringComparer.OrdinalIgnoreCase));

    private static bool EndsWithAny(string name, string[] suffixes) =>
        suffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    private static bool IsTestName(string name) =>
        TestInfixes.Any(i => name.Contains(i, StringComparison.OrdinalIgnoreCase))
        || name.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("test_", StringComparison.OrdinalIgnoreCase);

    private static bool IsDotfile(string name) => name.Length > 0 && name[0] == '.';
}
