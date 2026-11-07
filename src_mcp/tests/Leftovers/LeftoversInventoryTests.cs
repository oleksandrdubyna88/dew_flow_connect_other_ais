using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// The expected-exemption inventory (E5 S6, <c>todo/PLAN_test_leftovers_E5_S6_brief.md</c> § 7.3, § 14 R10 and R23): the markers on disk equal <c>shared/leftovers-exemptions.json</c>, each row holds the hash of its
/// site's trimmed line, and the count of <c>raw-temp</c> rows at creation is a ceiling that only goes down.
/// </summary>
public sealed class LeftoversInventoryTests : IDisposable
{
    /// <summary>
    /// The number of <c>raw-temp</c> rows when the inventory was created. LOWER it when a row goes; never raise it: a raise is a change of the plan, and this line makes it a visible diff that the review refuses.
    /// </summary>
    private const int RawTempRowsAtCreation = 77;

    private static readonly string[] ProofKinds = ["entry", "library", "read", "unreached", "reached", "bounded-exception"];

    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    private static string Marker(string cls = "raw-temp") => $"// {LeftoversGuard.CamelWord}-exempt: {cls} — a reason with enough words\n";

    private static string Site(string what) => PlantedLines.CSharp(what);

    private string NewRepo(string what, IReadOnlyDictionary<string, string> files)
    {
        var dir = _owned.Dir(what);
        RunGit(dir, "init", "-q");
        var top = RunGit(dir, "rev-parse", "--show-toplevel").Trim();
        foreach (var (name, text) in files)
        {
            var target = Path.Combine(top, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text, new UTF8Encoding(false));
        }

        return top;
    }

    private static string RunGit(string dir, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(60)).Should().BeTrue("git must not hang");
        process.ExitCode.Should().Be(0, error.Result);

        return output;
    }

    private static string InventoryText(IEnumerable<(string File, int Line, string Sha, string Class, string[] Guards)> rows) =>
        JsonSerializer.Serialize(new
        {
            about = "a test inventory",
            rows = rows.Select(r => new { file = r.File, line = r.Line, sha256 = r.Sha, @class = r.Class, guards = r.Guards, proof = "unreached" }),
            namedBy = Array.Empty<object>(),
        });

    private static string Hash(string root, string file, int line) => LeftoversBaseline.SiteHashes(root, RepositoryRoot.Path, [(file, line)])[0];

    private static List<string> Differences(string root) => [.. LeftoversInventory.Differences(root, RepositoryRoot.Path)];

    private const string File1 = "src_mcp/tests/A.cs";

    [Fact]
    public void ATreeAndAnInventoryThatAgree_HaveNoDifference()
    {
        var root = NewRepo("s6-inv-agree", new Dictionary<string, string> { [File1] = Marker() + Site("a") + "\n" });
        var sha = Hash(root, File1, 2);
        Directory.CreateDirectory(Path.Combine(root, "shared"));
        File.WriteAllText(Path.Combine(root, "shared", "leftovers-exemptions.json"), InventoryText([(File1, 2, sha, "raw-temp", ["csharp"])]));

        Differences(root).Should().BeEmpty();
    }

    [Fact]
    public void AMarkerWithNoRow_ARowWithNoMarker_ABlankLineAboveAMarker_AndAChangedSite_AreEachReported()
    {
        var root = NewRepo("s6-inv-diff", new Dictionary<string, string> { [File1] = Marker() + Site("a") + "\n" });
        var sha = Hash(root, File1, 2);
        Directory.CreateDirectory(Path.Combine(root, "shared"));
        var inventory = Path.Combine(root, "shared", "leftovers-exemptions.json");

        File.WriteAllText(inventory, InventoryText([]));
        Differences(root).Should().Equal(["in the tree, not in the inventory: src_mcp/tests/A.cs:2:raw-temp"]);

        File.WriteAllText(inventory, InventoryText([(File1, 2, sha, "raw-temp", ["csharp"]), ("src_mcp/tests/Gone.cs", 4, sha, "raw-temp", ["csharp"])]));
        Differences(root).Should().Equal(["in the inventory, not in the tree: src_mcp/tests/Gone.cs:4:raw-temp"]);

        File.WriteAllText(inventory, InventoryText([(File1, 2, sha, "raw-temp", ["csharp"])]));
        File.WriteAllText(Path.Combine(root, File1), "\n" + Marker() + Site("a") + "\n");
        Differences(root).Should().BeEquivalentTo(["in the tree, not in the inventory: src_mcp/tests/A.cs:3:raw-temp", "in the inventory, not in the tree: src_mcp/tests/A.cs:2:raw-temp"], "a moved marker is both");

        File.WriteAllText(Path.Combine(root, File1), Marker() + Site("changed") + "\n");
        Differences(root).Should().ContainSingle().Which.Should().StartWith("the site changed under its marker: src_mcp/tests/A.cs:2:raw-temp").And.Contain(sha);
    }

    [Fact]
    public void AMarkerOnTheSitesOwnLine_HasItsSiteOnThatLine_AndARowThatNamesOnlyNodeIsNotJudgedHere()
    {
        var line = Site("same") + " // " + LeftoversGuard.CamelWord + "-exempt: raw-temp — a reason with enough words";
        var root = NewRepo("s6-inv-same", new Dictionary<string, string> { [File1] = line + "\n" });
        var sha = Hash(root, File1, 1);
        Directory.CreateDirectory(Path.Combine(root, "shared"));
        File.WriteAllText(
            Path.Combine(root, "shared", "leftovers-exemptions.json"),
            InventoryText([(File1, 1, sha, "raw-temp", ["csharp"]), ("deploy/x.sh", 3, sha, "raw-temp", ["node"])]));

        Differences(root).Should().BeEmpty();
    }

    [Fact]
    public void TheRawTempRows_AreNotMoreThanAtCreation()
    {
        var rows = LeftoversInventory.Read(RepositoryRoot.Path).Rows;

        rows.Count(row => row.Class == "raw-temp").Should().BeLessThanOrEqualTo(RawTempRowsAtCreation, "the count at creation binds: a row only goes away, and this constant goes down with it");
    }

    [Fact]
    public void TheRealInventory_IsWellFormed_EveryRawTempRowHasOneKind_AndNoRowIsListedTwice()
    {
        var document = LeftoversInventory.Read(RepositoryRoot.Path);

        document.Rows.Should().NotBeEmpty();
        document.Rows.Select(row => $"{row.File}:{row.Line}:{row.Class}").Should().OnlyHaveUniqueItems();
        document.Rows.Should().OnlyContain(row => row.Sha256.Length == 64 && row.Guards.Count > 0 && row.Guards.All(g => g == "csharp" || g == "node"));
        document.Rows.Where(row => row.Class == "raw-temp").Should().OnlyContain(row => row.Proof != null && ProofKinds.Contains(row.Proof), "each raw-temp row is proved by ONE kind");
        document.Rows.Where(row => row.Class != "raw-temp").Should().OnlyContain(row => row.Proof == null);
        document.NamedBy.Should().OnlyContain(pair => pair.Why.Trim().Length > 0);
    }
}
