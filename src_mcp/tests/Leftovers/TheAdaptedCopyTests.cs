using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// The diff check of the adapted copy (E5 S3, plan D2). The leftovers helper in <c>src_mcp/tests_leftovers</c> and <c>src_mcp/tests/Leftovers</c> is a copy of
/// <c>dew_flow_mcp</c>'s <c>tests/Mcp.Tests/Leftovers</c> at one commit, changed ONLY by the exact replaces <c>template-manifest.json</c> lists. This check undoes
/// those replaces on the file's raw bytes and compares the git blob SHA with the manifest's. It needs no network and holds no copy of the template's bytes.
/// </summary>
/// <remarks>
/// <para>When it is red, the message names the file, the operation (or both SHAs) and the command that shows the template's blob. With
/// <c>COAI_TEMPLATE_REPO</c> naming a local <c>dew_flow_mcp</c> checkout it also names the first line that differs (<c>git cat-file</c>, still no network).</para>
/// <para>Never edit a copied file to satisfy a coai-only check: this test would be red, and rightly. Change the manifest in the same commit, by reviewed operation.</para>
/// </remarks>
public sealed class TheAdaptedCopyTests
{
    /// <summary>Names a local checkout of the template repository, so a red check can name the first differing line.</summary>
    public const string TemplateRepoVariable = "COAI_TEMPLATE_REPO";

    private static readonly string[] CopiedUnder = ["src_mcp/tests_leftovers/", "src_mcp/tests/Leftovers/"];

    /// <summary>
    /// The coai-owned <c>.cs</c> files of the two folders — every other one must be a manifest target. A new file that is on neither list is red, so a copy
    /// cannot slip in unchecked. A path is relative to the repository root.
    /// </summary>
    private static readonly string[] CoaiOwn =
    [
        "src_mcp/tests/Leftovers/TheAdaptedCopyTests.cs",
        "src_mcp/tests/Leftovers/TheHelpersTestsMakeNothingOutsideAnOwnedScopeTests.cs",
        "src_mcp/tests_leftovers/RunTemp.cs",
        "src_mcp/tests_leftovers/RunTempFolder.cs",
        "src_mcp/tests_leftovers/RunTempFixture.cs",
        "src_mcp/tests_leftovers/TempDir.cs",
        "src_mcp/tests_leftovers/OwnerClaim.cs",
        "src_mcp/tests_leftovers/SuiteChild.cs",
    ];

    internal sealed record Operation(string Old, string New, int Count);

    internal sealed record Entry(string Upstream, string Target, string Blob, IReadOnlyList<Operation> Replace);

    private sealed record Manifest(string Repository, string Commit, IReadOnlyList<Entry> Entries);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string ManifestPath => Path.Combine(RepositoryRoot.Path, "src_mcp", "tests_leftovers", "template-manifest.json");

    private static Manifest ReadManifest() =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath), Json) ?? throw new InvalidDataException("template-manifest.json is empty");

    /// <summary>One row per manifest entry: the theory below names the file that is red.</summary>
    public static TheoryData<string> ManifestTargets()
    {
        var rows = new TheoryData<string>();
        foreach (var entry in ReadManifest().Entries)
        {
            rows.Add(entry.Target);
        }

        return rows;
    }

    [Fact]
    public void TheManifest_IsWellFormed()
    {
        var manifest = ReadManifest();

        manifest.Commit.Should().MatchRegex("^[0-9a-f]{40}$", "the template commit is a full SHA");
        manifest.Entries.Should().NotBeEmpty();
        manifest.Entries.Select(e => e.Blob).Should().OnlyContain(b => IsLowerHex40(b), "a blob is a 40-digit lowercase hex git object id");
        manifest.Entries.Select(e => e.Target).Should().OnlyHaveUniqueItems("a target is copied once");
        manifest.Entries.Select(e => e.Target).Should().OnlyContain(t => CopiedUnder.Any(f => t.StartsWith(f, StringComparison.Ordinal)), "a copy lives in one of the two helper folders");
        manifest.Entries.Select(e => e.Upstream).Should().OnlyHaveUniqueItems("an upstream file is copied once");
        manifest.Entries.Select(e => e.Upstream).Should().OnlyContain(u => u.StartsWith("tests/Mcp.Tests/", StringComparison.Ordinal), "the template's helper lives under tests/Mcp.Tests/");
        foreach (var operation in manifest.Entries.SelectMany(e => e.Replace))
        {
            operation.Old.Should().NotBeNullOrEmpty();
            operation.New.Should().NotBeNullOrEmpty();
            operation.New.Should().NotBe(operation.Old, "an operation that changes nothing is not an operation");
            operation.Count.Should().BeGreaterThanOrEqualTo(1);
        }
    }

    [Theory]
    [MemberData(nameof(ManifestTargets))]
    public async Task EveryCopiedFile_IsTheTemplateUnderTheListedReplacesOnly(string target)
    {
        var manifest = ReadManifest();
        var entry = manifest.Entries.Single(e => e.Target == target);

        var verdict = Verify(entry, File.ReadAllBytes(Path.Combine(RepositoryRoot.Path, target.Replace('/', Path.DirectorySeparatorChar))));

        if (verdict.Failure is { } failure)
        {
            Assert.Fail(failure + await WhereItDiffersAsync(entry, verdict.Reversed));
        }
    }

    [Fact]
    public void EveryLeftoversFile_IsInTheManifestOrIsCoaisOwn()
    {
        var targets = ReadManifest().Entries.Select(e => e.Target).ToHashSet(StringComparer.Ordinal);
        var found = CopiedUnder
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, folder.TrimEnd('/')), "*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(segment => segment is "bin" or "obj"))
            .ToList();

        found.Should().Contain(targets, "every manifest target exists");
        found.Where(path => !targets.Contains(path) && !CoaiOwn.Contains(path)).Should().BeEmpty(
            "a .cs file in the helper folders is a manifest entry or on CoaiOwn, with its reason: a copy cannot slip in unchecked");
        CoaiOwn.Should().NotIntersectWith(targets, "a file is either a copy or coai's own");
    }

    /// <summary>
    /// A manifest count BELOW the template's occurrences: the template has the old text twice, the manifest lists a count of one, and the copy replaced only one of them. Undoing the
    /// replace gives the template back byte for byte, so the blob matches — only applying the operations FORWARD again shows that one old text remains.
    /// </summary>
    [Fact]
    public void ACountBelowTheTemplatesOccurrences_IsRefused_WhereOneOldTextRemainsInTheCopy()
    {
        var template = Encoding.UTF8.GetBytes("aXbXc");
        var entry = new Entry("tests/Mcp.Tests/Fake.cs", "src_mcp/tests_leftovers/Fake.cs", GitBlobSha(template), [new Operation("X", "Y", 1)]);

        var partial = Verify(entry, Encoding.UTF8.GetBytes("aYbXc"));
        var faithful = Verify(entry with { Replace = [new Operation("X", "Y", 2)] }, Encoding.UTF8.GetBytes("aYbYc"));

        partial.Failure.Should().NotBeNull("the copy kept one old text the manifest does not list").And.Contain("2 occurrence(s) of the old text").And.Contain("lists 1");
        faithful.Failure.Should().BeNull("a copy with the right count passes");
    }

    /// <summary>The outcome of the check: a message when it failed, and the reversed bytes either way.</summary>
    internal sealed record Verdict(string? Failure, byte[] Reversed);

    /// <summary>Undoes the entry's operations in REVERSE order on the bytes, then compares the git blob SHA. Pure: the bytes in, the verdict out.</summary>
    internal static Verdict Verify(Entry entry, byte[] bytes)
    {
        var current = bytes;
        for (var index = entry.Replace.Count - 1; index >= 0; index--)
        {
            var operation = entry.Replace[index];
            var (found, lines) = Occurrences(current, Encoding.UTF8.GetBytes(operation.New));
            if (found != operation.Count)
            {
                return new Verdict(
                    $"{entry.Target}: operation {index + 1} of {entry.Replace.Count} ('{Shown(operation.Old)}' -> '{Shown(operation.New)}') expected {operation.Count} occurrence(s) of the new text, found {found}"
                    + (lines.Count > 0 ? $" at line(s) {string.Join(", ", lines)}" : " (none)")
                    + ". The copy changed in a way the manifest does not list.",
                    current);
            }

            current = Replace(current, Encoding.UTF8.GetBytes(operation.New), Encoding.UTF8.GetBytes(operation.Old));
        }

        var actual = GitBlobSha(current);
        if (actual != entry.Blob)
        {
            return Mismatch(entry, actual, current);
        }

        // The same operations FORWARD on the template's bytes must give exactly the committed file, each with its listed count of old text. Undoing alone cannot see a count
        // BELOW the template's: the copy that replaced one of two old texts reverses to the template, and only the forward count (two, not one) shows what stayed.
        var forward = current;
        for (var index = 0; index < entry.Replace.Count; index++)
        {
            var operation = entry.Replace[index];
            var oldBytes = Encoding.UTF8.GetBytes(operation.Old);
            var (found, lines) = Occurrences(forward, oldBytes);
            if (found != operation.Count)
            {
                return new Verdict(
                    $"{entry.Target}: operation {index + 1} of {entry.Replace.Count} ('{Shown(operation.Old)}' -> '{Shown(operation.New)}') applied to the template finds {found} occurrence(s) of the old text"
                    + (lines.Count > 0 ? $" at line(s) {string.Join(", ", lines)}" : string.Empty)
                    + $", but the manifest lists {operation.Count}: the copy keeps old text the manifest does not account for.",
                    current);
            }

            forward = Replace(forward, oldBytes, Encoding.UTF8.GetBytes(operation.New));
        }

        return forward.AsSpan().SequenceEqual(bytes)
            ? new Verdict(null, current)
            : new Verdict($"{entry.Target}: the listed replaces applied to the template do not give this file byte for byte.", current);
    }

    private static Verdict Mismatch(Entry entry, string actual, byte[] current) =>
        new Verdict(
            $"{entry.Target}: with the listed replaces undone, the git blob is {actual}; the manifest says {entry.Blob} (template file {entry.Upstream}). "
            + $"A byte of the copy differs from the template outside the listed replaces. Compare: git -C <dew_flow_mcp> show {entry.Blob} | diff - <(the reversed bytes). ",
            current);

    private static string Shown(string text) => text.Length <= 60 ? text : text[..57] + "...";

    private static bool IsLowerHex40(string text) => text.Length == 40 && text.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    /// <summary>The git object id of a file's content: <c>sha1("blob " + length + NUL + bytes)</c>. Not a security use of SHA-1; it is the name git gives the blob.</summary>
    internal static string GitBlobSha(byte[] bytes)
    {
        var header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
#pragma warning disable CA5350 // git names a blob by SHA-1; the manifest's golden value is that name, not a secret or a signature.
        return Convert.ToHexStringLower(SHA1.HashData([.. header, .. bytes]));
#pragma warning restore CA5350
    }

    private static (int Count, List<int> Lines) Occurrences(byte[] haystack, byte[] needle)
    {
        var lines = new List<int>();
        var from = 0;
        while (haystack.AsSpan(from).IndexOf(needle) is var offset and >= 0)
        {
            var at = from + offset;
            lines.Add(1 + haystack.AsSpan(0, at).Count((byte)'\n'));
            from = at + needle.Length;
        }

        return (lines.Count, lines);
    }

    private static byte[] Replace(byte[] haystack, byte[] oldBytes, byte[] newBytes)
    {
        var result = new List<byte>(haystack.Length);
        var from = 0;
        while (haystack.AsSpan(from).IndexOf(oldBytes) is var offset and >= 0)
        {
            result.AddRange(haystack.AsSpan(from, offset));
            result.AddRange(newBytes);
            from += offset + oldBytes.Length;
        }

        result.AddRange(haystack.AsSpan(from));
        return [.. result];
    }

    /// <summary>With a local template checkout named, the first line where the reversed bytes differ from the template's blob; otherwise nothing, and no upstream bytes are read.</summary>
    private static async Task<string> WhereItDiffersAsync(Entry entry, byte[] reversed)
    {
        if (Environment.GetEnvironmentVariable(TemplateRepoVariable) is not { Length: > 0 } repo)
        {
            return $"(Set {TemplateRepoVariable} to a local dew_flow_mcp checkout to name the first differing line.)";
        }

        var result = await new ProcessLauncher().RunAsync(
            new ProcessRequest("git", ["-C", repo, "cat-file", "blob", entry.Blob], AppContext.BaseDirectory) { Timeout = TimeSpan.FromSeconds(30) },
            TestContext.Current.CancellationToken);
        if (result.ExitCode != 0)
        {
            return $"(git -C {repo} cat-file blob {entry.Blob} failed: {result.StdErr.Trim()})";
        }

        var template = result.StdOut.Split('\n');
        var mine = Encoding.UTF8.GetString(reversed).Split('\n');
        for (var line = 0; line < Math.Max(template.Length, mine.Length); line++)
        {
            var left = line < template.Length ? template[line] : "<end of file>";
            var right = line < mine.Length ? mine[line] : "<end of file>";
            if (left != right)
            {
                return $"First differing line {line + 1}: template '{Shown(left)}' vs copy (listed replaces undone) '{Shown(right)}'.";
            }
        }

        return "(Line by line the text is equal: the difference is a line ending or a trailing byte.)";
    }
}
