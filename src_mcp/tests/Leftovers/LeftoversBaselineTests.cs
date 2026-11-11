using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// The C# half of the <c>raw-temp</c> baseline's second pass (E5 S6, <c>todo/PLAN_test_leftovers_E5_S6_brief.md</c> § 7.2, § 14 R8, R9, R14, § 16). Every case builds a PRIVATE git repository in an owned folder, scans it with the
/// guard, and applies the baseline through the shipped library. The repository root is whatever git says it is, never the folder as it was built: on the hosted Windows runner the temp path holds a short name (<c>RUNNER~1</c>).
/// </summary>
public sealed class LeftoversBaselineTests : IDisposable
{
    private const string Root = "src_mcp/tests/";
    private const string Proof = "src_mcp/tests/RunTempTests.cs";

    /// <summary>A line the guard calls <c>raw-temp</c>: this file is read by the guard it tests, so the API name is never spelled whole (see <see cref="PlantedLines"/>).</summary>
    private static readonly string Planted = PlantedLines.CSharp("x");

    private static readonly string Other = PlantedLines.CSharp("y");

    private static string ProofMarker(string root) => $"// {LeftoversGuard.CamelWord}-baseline-proof: {root}\n";

    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    private static string Git(string dir, params string[] arguments)
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

    /// <summary>A private repository: its root as git spells it, the files written, the baseline written when given.</summary>
    private string NewRepo(string what, IReadOnlyDictionary<string, string> files, string? baseline)
    {
        var dir = _owned.Dir(what);
        Git(dir, "init", "-q");
        var top = Git(dir, "rev-parse", "--show-toplevel").Trim();
        foreach (var (name, text) in files)
        {
            var target = Path.Combine(top, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text, new UTF8Encoding(false));
        }

        if (baseline is not null)
        {
            File.WriteAllText(Path.Combine(top, "raw-temp-baseline.json"), baseline, new UTF8Encoding(false));
        }

        return top;
    }

    private static string Sha(string root, string file, int line) => LeftoversBaseline.SiteHashes(root, RepositoryRoot.Path, [(file, line)])[0];

    private static string BaselineText(string scopes, string entries, string cls = "raw-temp") =>
        "{\n  \"about\": \"a test baseline\",\n  \"class\": \"" + cls + "\",\n  \"scopes\": [" + scopes + "],\n  \"entries\": [" + entries + "]\n}\n";

    private static string Scope(string root = Root, string proof = Proof, string guard = "csharp") => $"{{ \"root\": \"{root}\", \"proof\": \"{proof}\", \"guard\": \"{guard}\" }}";

    private static string Entry(string file, string sha, int line) => $"{{ \"file\": \"{file}\", \"sha256\": \"{sha}\", \"line\": {line} }}";

    /// <summary>The findings after the baseline, as <c>file:line:class</c>.</summary>
    private static List<string> After(string root, string node = "node", string? mount = null)
    {
        var report = LeftoversGuard.Scan(root, RepositoryRoot.Path);

        return [.. LeftoversBaseline.Apply(report, root, mount ?? RepositoryRoot.Path, node).Select(f => $"{f.File}:{f.Line}:{f.Class}")];
    }

    private Dictionary<string, string> Tree(string body) =>
        new() { [Proof] = ProofMarker(Root) + "// a proof file\n", [$"{Root}A.cs"] = body };

    [Fact]
    public void ALineTheBaselineHolds_IsPassed_AndAMovedLineKeepsItsEntry()
    {
        var path = $"{Root}A.cs";
        var root = NewRepo("s6-pass", Tree($"\n\n{Planted}\n"), null);
        var sha = Sha(root, path, 3);
        File.WriteAllText(Path.Combine(root, "raw-temp-baseline.json"), BaselineText(Scope(), Entry(path, sha, 1)));

        After(root).Should().BeEmpty("the line moved from the hint 1 to line 3 and keeps its entry: the line is a hint, the hash is the key");
    }

    [Fact]
    public void ASecondIdenticalLine_IsAFinding_AndTheFirstOnePasses()
    {
        var path = $"{Root}A.cs";
        var root = NewRepo("s6-second", Tree($"{Planted}\n{Planted}\n"), null);
        File.WriteAllText(Path.Combine(root, "raw-temp-baseline.json"), BaselineText(Scope(), Entry(path, Sha(root, path, 1), 1)));

        After(root).Should().Equal([$"{path}:2:raw-temp"], "a multiset: one entry passes one occurrence, the second is beyond it");
    }

    [Fact]
    public void AnEntrySwappedUnderTheSameTotal_IsOneRawTempAndOneBaselineFinding()
    {
        var path = $"{Root}A.cs";
        var root = NewRepo("s6-swap", Tree($"{Planted}\n{Other}\n"), null);
        var missing = Entry(path, new string('0', 64), 2);
        File.WriteAllText(Path.Combine(root, "raw-temp-baseline.json"), BaselineText(Scope(), Entry(path, Sha(root, path, 1), 1) + ", " + missing));

        After(root).Should().BeEquivalentTo([$"{path}:2:raw-temp", $"{path}:2:baseline"]);
    }

    [Fact]
    public void AStaleEntry_IsABaselineFinding()
    {
        var path = $"{Root}A.cs";
        var root = NewRepo("s6-stale", Tree($"{Planted}\n"), null);
        File.WriteAllText(Path.Combine(root, "raw-temp-baseline.json"), BaselineText(Scope(), Entry(path, Sha(root, path, 1), 1) + ", " + Entry(path, new string('1', 64), 9)));

        After(root).Should().Equal([$"{path}:9:baseline"]);
    }

    [Fact]
    public void AnEntryOutsideEveryScope_ClearsNothing_AndIsReported()
    {
        var outside = "src_mcp/other/B.cs";
        var files = Tree($"{Planted}\n");
        files[outside] = $"{Planted}\n";
        var root = NewRepo("s6-outside", files, null);
        File.WriteAllText(Path.Combine(root, "raw-temp-baseline.json"), BaselineText(Scope(), Entry(outside, new string('2', 64), 1)));

        After(root).Should().Contain($"{outside}:1:baseline").And.Contain($"{Root}A.cs:1:raw-temp");
    }

    [Fact]
    public void AProofThatIsAbsent_OrHasNoMarker_OrTheMarkerOfAnotherRoot_ClearsNothing()
    {
        var path = $"{Root}A.cs";
        foreach (var (name, proofText) in new[] { ("s6-noproof", (string?)null), ("s6-nomarker", "// nothing\n"), ("s6-otherroot", ProofMarker("src_server/tests/")) })
        {
            var files = new Dictionary<string, string> { [path] = $"{Planted}\n" };
            if (proofText is not null)
            {
                files[Proof] = proofText;
            }

            var root = NewRepo(name, files, null);
            File.WriteAllText(Path.Combine(root, "raw-temp-baseline.json"), BaselineText(Scope(), Entry(path, Sha(root, path, 1), 1)));

            var found = After(root);

            found.Should().Contain(finding => finding.EndsWith(":baseline", StringComparison.Ordinal), name);
            found.Should().Contain($"{path}:1:raw-temp", name + ": no entry of a scope whose proof does not hold applies");
        }
    }

    [Fact]
    public void AClassOtherThanRawTemp_AndAMalformedFile_AreOneBaselineFindingEach_AndApplyNothing()
    {
        var path = $"{Root}A.cs";
        var root = NewRepo("s6-class", Tree($"{Planted}\n"), null);
        File.WriteAllText(Path.Combine(root, "raw-temp-baseline.json"), BaselineText(Scope(), Entry(path, Sha(root, path, 1), 1), cls: "raw-worktree"));
        After(root).Should().Contain("raw-temp-baseline.json:1:baseline").And.Contain($"{path}:1:raw-temp");

        File.WriteAllText(Path.Combine(root, "raw-temp-baseline.json"), "{ this is not json");
        After(root).Should().Contain("raw-temp-baseline.json:1:baseline").And.Contain($"{path}:1:raw-temp");
    }

    [Fact]
    public void AScopeOfAnotherGuard_IsNotJudgedByThisOne()
    {
        var path = $"{Root}A.cs";
        var node = "src_vs_code/src/test/";
        var files = Tree($"{Planted}\n");
        files[node + "runFolder.test.mjs"] = ProofMarker(node);
        var root = NewRepo("s6-other-guard", files, null);
        File.WriteAllText(
            Path.Combine(root, "raw-temp-baseline.json"),
            BaselineText(Scope() + ", " + Scope(node, node + "runFolder.test.mjs", "node"), Entry(path, Sha(root, path, 1), 1) + ", " + Entry(node + "a.test.mjs", new string('4', 64), 1)));

        After(root).Should().BeEmpty("a node scope with a stale entry is the Node guard's to report, and this guard's own scope is clean");
    }

    [Fact]
    public void NodeThatCannotBeStarted_IsOneBaselineFinding_NotAnException()
    {
        var root = NewRepo("s6-nonode", Tree($"{Planted}\n"), null);

        var found = After(root, node: "no-such-node-zz9");

        found.Should().Contain("raw-temp-baseline.json:1:baseline");
        found.Should().Contain($"{Root}A.cs:1:raw-temp", "nothing is passed when the baseline could not be applied");
    }

    [Fact]
    public void AModuleThatIsMissing_IsOneBaselineFinding_NotAPass()
    {
        var root = NewRepo("s6-nomodule", Tree($"{Planted}\n"), null);
        var emptyMount = _owned.Dir("s6-empty-mount");

        var found = After(root, mount: emptyMount);

        found.Should().Contain("raw-temp-baseline.json:1:baseline");
    }

    [Fact]
    public void The_drained_baseline_file_stays_gone()
    {
        var path = Path.Combine(RepositoryRoot.Path, "raw-temp-baseline.json");

        (File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null).Should().BeFalse("a repository with none ships none: the baseline was drained to zero and deleted (E5 S7-d), and the ratchet refuses a re-creation");
    }

    /// <summary>
    /// F24's second acceptance check, the C# half (D8 (l)): for each of the four <c>csharp</c> scopes, the REAL proof file with its marker taken out leaves one <c>baseline</c> finding naming <c>proof missing</c>; with the
    /// marker in place the scope's proof holds. The forced runs themselves (exit 78) are the proof files' own tests; this is the part of the check that closes the scope.
    /// </summary>
    [Theory]
    [InlineData("src_mcp/tests/", "src_mcp/tests/RunTempTests.cs")]
    [InlineData("src_server/tests/", "src_server/tests/RunFolderTests.cs")]
    [InlineData("src_bugs/tests/", "src_bugs/tests/RunFolderTests.cs")]
    [InlineData("src_bench/CoaiBench.Tests/", "src_bench/CoaiBench.Tests/RunFolderTests.cs")]
    public void TheProofCheck_ClosesForEachScope(string root, string proof)
    {
        var real = File.ReadAllText(Path.Combine(RepositoryRoot.Path, proof.Replace('/', Path.DirectorySeparatorChar)));
        var withoutMarker = string.Join('\n', real.Split('\n').Where(line => !line.Contains($"{LeftoversGuard.CamelWord}-baseline-proof:", StringComparison.Ordinal)));
        var baseline = BaselineText(Scope(root, proof), string.Empty);

        var closed = BaselineFindings(NewRepo("s6-proof-missing", new Dictionary<string, string> { [proof] = withoutMarker }, baseline));
        var held = BaselineFindings(NewRepo("s6-proof-held", new Dictionary<string, string> { [proof] = real }, baseline));

        closed.Should().ContainSingle().Which.Should().Contain("proof missing", $"the marker line of {proof} is taken out of the copy");
        held.Should().BeEmpty($"the real {proof} carries the marker for exactly {root}");
    }

    private static List<string> BaselineFindings(string root)
    {
        var report = LeftoversGuard.Scan(root, RepositoryRoot.Path);

        return [.. LeftoversBaseline.Apply(report, root, RepositoryRoot.Path).Where(f => f.Class == "baseline").Select(f => f.Text)];
    }
}

/// <summary>
/// Lines the guards call <c>raw-temp</c>, for the tests that plant one. The guards read this folder too, and the C# guard JOINS neighbouring literals (<c>"a" + "b"</c>) before it looks, so a line is never written in pieces:
/// it is written with a tilde inside the API name and the tilde is taken out when the test runs.
/// </summary>
internal static class PlantedLines
{
    internal static string CSharp(string what) => ("var d = Directory.Create~TempSubdirectory(\"" + what + "\");").Replace("~", string.Empty, StringComparison.Ordinal);

    internal static string Mjs() => "const d = mkd~tempSync(join(tm~pdir(), 'x-'));".Replace("~", string.Empty, StringComparison.Ordinal);

    internal static string Shell() => "d=$(mk~temp -d)".Replace("~", string.Empty, StringComparison.Ordinal);
}
