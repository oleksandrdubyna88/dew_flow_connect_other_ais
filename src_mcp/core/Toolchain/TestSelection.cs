using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>The files a selected run is given: <c>files</c>, <c>none</c> (nothing the job changed is under a test prefix), <c>too-many</c> (over <see cref="TestSelection.MaxFiles"/>) or <c>unsafe</c> (a name or a stem that is not safe).</summary>
public sealed record SelectedFiles(string State, IReadOnlyList<string> Files)
{
    public static SelectedFiles None { get; } = new("none", []);
}

/// <summary>
/// <b>Selection, level two</b> (E4 s5 t3; SPEC E): a selected run exists only where the toolchain FILE names <c>testFor</c> — the repository's own word for "run the tests of these files". The files are what the job added or changed (base to head) under a <c>testFiles</c> prefix of the project;
/// <c>{file}</c> and <c>{stem}</c> are replaced in place, one item per file. S6: a stem that is not letters, digits and underscores, an unsafe file name, or more than <see cref="MaxFiles"/> files means no selected run. A detected project never has one. Pure.
/// </summary>
public static partial class TestSelection
{
    public const int MaxFiles = 50;

    /// <summary>The files of <paramref name="changed"/> (added or modified paths) inside the project and under one of <paramref name="testFiles"/> (folder prefixes relative to the project).</summary>
    public static SelectedFiles Select(IEnumerable<string> changed, string projectFolder, IReadOnlyList<string> testFiles)
    {
        var files = changed.Where(path => IsUnder(path, projectFolder, testFiles)).Distinct(StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            return SelectedFiles.None;
        }

        if (files.Count > MaxFiles)
        {
            return new SelectedFiles("too-many", []);
        }

        return files.All(IsSafe) ? new SelectedFiles("files", files) : new SelectedFiles("unsafe", []);
    }

    /// <summary>The argv of the selected run: <c>{file}</c> becomes <c>./&lt;path from the working folder&gt;</c> and <c>{stem}</c> the file name without its extension, each in place and one item per file; <c>{jobs}</c> is filled.</summary>
    public static IReadOnlyList<string> Argv(IReadOnlyList<string> testFor, IReadOnlyList<string> allFiles, string workingFolder, int jobs)
    {
        // M1 of the s5 verify: a name that is not a safe path is never an item of an argv, whoever the caller is (a name taken from a test's OUTPUT is the head's text).
        var files = allFiles.Where(IsSafe).ToList();
        var folder = workingFolder == "." ? string.Empty : workingFolder;
        var result = new List<string>();
        foreach (var item in testFor)
        {
            if (item == "{file}")
            {
                result.AddRange(files.Select(path => "./" + (folder.Length > 0 && path.StartsWith(folder + "/", StringComparison.Ordinal) ? path[(folder.Length + 1)..] : path)));
            }
            else if (item == "{stem}")
            {
                result.AddRange(files.Select(Stem));
            }
            else
            {
                result.Add(item.Replace("{jobs}", jobs.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
            }
        }

        return result;
    }

    private static string Combine(string folder, string prefix)
    {
        var inside = prefix.Trim('/');

        return folder.Length == 0 || folder == "." ? inside : inside.Length == 0 ? folder : folder + "/" + inside;
    }

    private static string Stem(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');

        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>
    /// Whether <paramref name="path"/> (relative to the repository root) is inside one of the <paramref name="testFiles"/> prefixes. The prefixes are relative to the PROJECT (<paramref name="projectFolder"/>, relative to the repository root; empty or <c>.</c> is the root):
    /// the selected run (<see cref="Select"/>) and the narrowed re-run read them through this one method, so they cannot read them two ways (E4 s5 t6 d3). An empty prefix is the whole project.
    /// </summary>
    public static bool IsUnder(string path, string projectFolder, IReadOnlyList<string> testFiles) =>
        testFiles.Select(prefix => Combine(projectFolder, prefix)).Any(prefix => prefix.Length == 0 || path.StartsWith(prefix + "/", StringComparison.Ordinal));

    /// <summary>A path a test tool may be given: a closed set of characters, no segment starting with <c>-</c> and none that is <c>.</c> or <c>..</c>, and a stem of letters, digits and underscores.</summary>
    public static bool IsSafe(string path)
    {
        try
        {
            return SafePath().IsMatch(path) && !path.Split('/').Any(segment => segment.StartsWith('-') || segment is "." or ".." or "") && StemShape().IsMatch(Stem(path));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._/+@-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SafePath();

    [GeneratedRegex(@"^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StemShape();
}
