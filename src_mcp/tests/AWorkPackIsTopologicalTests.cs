using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>WorkPack</c> (epoch 3 E3 s3 t3): the pack of a scope as a pure function — feature.yaml, the plan file, the ancestors, then the stories and tasks so that every node comes after the nodes it depends on, with its limits, its fences,
/// its line Not sent and the approval lines. The vectors are <c>shared/work-pack-vectors.json</c>.
/// </summary>
public sealed class AWorkPackIsTopologicalTests
{
    private const string Feature = "plan";

    private static readonly StrictYamlReader Reader = new();

    public static TheoryData<string> VectorNames => [.. PackVectors.All().Select(vector => vector.Name)];

    private static PackOutcome Build(IReadOnlyDictionary<string, string> files, string scope = "") =>
        WorkPack.Build(new ListedFolder(files), Reader, Feature, scope.Length == 0 ? Feature : $"{Feature}/{scope}");

    private static Pack Packed(IReadOnlyDictionary<string, string> files, string scope = "")
    {
        var outcome = Build(files, scope);

        return outcome.Should().BeOfType<PackOutcome.Packed>(outcome is PackOutcome.Refused refused ? refused.Sentence : string.Empty).Subject.Pack;
    }

    private static string Refused(IReadOnlyDictionary<string, string> files, string scope = "") =>
        Build(files, scope).Should().BeOfType<PackOutcome.Refused>().Subject.Sentence;

    private static Dictionary<string, string> Plan() => new(PackVectors.All().Single(vector => vector.Name.StartsWith("a story scope holds", StringComparison.Ordinal)).Files);

    [Theory]
    [MemberData(nameof(VectorNames))]
    public void P1_P5_P6_P7_AVector_GivesItsOrderItsDependenciesOrItsRefusal(string name)
    {
        var vector = PackVectors.All().Single(one => one.Name == name);

        if (vector.Refused.Length > 0)
        {
            Refused(vector.Files, vector.Scope).Should().Contain(vector.Refused);

            return;
        }

        var pack = Packed(vector.Files, vector.Scope);

        pack.Sections.Select(section => section.Path).Should().Equal(vector.Order.Select(path => $"{Feature}/{path}"));
        pack.Approval.Where(line => line.Kind == "D").Select(line => (line.Key, line.Value)).Should().Equal(vector.Dependencies.Select(item => (item.Node, item.Title)));
        var at = vector.Order.Select(path => pack.Text.IndexOf($"## {Feature}/{path}\n", StringComparison.Ordinal)).ToList();
        at.Should().OnlyContain(index => index >= 0).And.BeInAscendingOrder("the sections stand in the order of the vector");
    }

    [Fact]
    public void D1_TheSameInput_GivesTheSameBytes_WhateverTheOrderTheFilesWereListedIn()
    {
        var files = Plan();
        var reversed = files.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);

        var first = Packed(files, "epoch-1/e2-more");
        var again = Packed(files, "epoch-1/e2-more");
        var other = Packed(reversed, "epoch-1/e2-more");

        again.Text.Should().Be(first.Text);
        other.Text.Should().Be(first.Text);
        other.ApprovalId.Should().Be(first.ApprovalId);
        first.Bytes.Should().Be(Encoding.UTF8.GetByteCount(first.Text));
    }

    [Fact]
    public void D1_EveryStoryOfTheV2FolderPacksOnItsOwn_InTheSameBytesTwice()
    {
        var root = ListedFolder.OfDisk(Path.Combine(RepoPaths.Root(), "v2"));
        var stories = root.Files.Where(file => file.Path.EndsWith("/story.yaml", StringComparison.Ordinal)).Select(file => file.Path[..^"/story.yaml".Length]).ToList();

        stories.Should().HaveCountGreaterThan(30);
        foreach (var story in stories)
        {
            var one = WorkPack.Build(root, Reader, "v2", $"v2/{story}");
            var two = WorkPack.Build(root, Reader, "v2", $"v2/{story}");

            one.Should().BeOfType<PackOutcome.Packed>(story);
            ((PackOutcome.Packed)two).Pack.Text.Should().Be(((PackOutcome.Packed)one).Pack.Text, story);
        }
    }

    [Fact]
    public void D1_ThePack_ReadsNoDiskAndNoClockAndNoEnvironment()
    {
        string[] forbidden = [@"System\.IO", @"\bFile\.", @"\bDirectory\.", @"\bPath\.Get", "DateTime", "TimeProvider", @"\bEnvironment\.", @"\bProcess"];
        var scanned = Directory.GetFiles(Path.Combine(RepoPaths.Root(), "src_mcp", "core", "Work"), "WorkPack*.cs");

        scanned.Should().NotBeEmpty();
        foreach (var file in scanned)
        {
            var code = string.Join("\n", File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            forbidden.Where(word => System.Text.RegularExpressions.Regex.IsMatch(code, word)).Should().BeEmpty($"{Path.GetFileName(file)} is pure");
        }

        // The companion: the same scan matches something where there is IO, so a clean answer above means something.
        var io = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src_mcp", "src", "Work", "DiskWorkFolder.cs"));
        forbidden.Where(word => System.Text.RegularExpressions.Regex.IsMatch(io, word)).Should().NotBeEmpty();
    }

    [Fact]
    public void P6_AScopeOutsideTheFeatureFolderIsRefused_AndSoIsAFolderThatIsNotAPlan()
    {
        var files = Plan();

        WorkPack.Build(new ListedFolder(files), Reader, Feature, "elsewhere/epoch-1").Should().BeOfType<PackOutcome.Refused>().Which.Sentence.Should().Contain("elsewhere/epoch-1").And.Contain("names no node");
        WorkPack.Build(new ListedFolder(files), Reader, Feature, $"{Feature}/architecture").Should().BeOfType<PackOutcome.Refused>().Which.Sentence.Should().Contain("names no node");
        WorkPack.Build(new ListedFolder(new Dictionary<string, string> { ["notes.md"] = "x\n" }), Reader, Feature, Feature).Should().BeOfType<PackOutcome.Refused>();
    }

    [Fact]
    public void P5_APackOverTheLimit_IsRefusedWithItsSizeAndTheThreeLargestSections_AndAPackUnderItIsNeverCut()
    {
        var vector = PackVectors.All().Single(one => one.Refused == "three largest");
        var sentence = Refused(vector.Files, vector.Scope);

        sentence.Should().Contain("262144").And.Contain("three largest");
        var largest = vector.Files.Where(file => file.Key.EndsWith("task.yaml", StringComparison.Ordinal)).OrderByDescending(file => file.Value.Length).Take(3).ToList();
        largest.Should().OnlyContain(file => sentence.Contains($"{Feature}/{file.Key}", StringComparison.Ordinal));
        sentence.Should().Contain($"{largest[0].Value.Length} bytes");

        var under = vector.Files.ToDictionary(file => file.Key, file => file.Value);
        under.Remove(under.Keys.Single(key => key.Contains("t5-e", StringComparison.Ordinal)));
        var pack = Packed(under, vector.Scope);
        pack.Bytes.Should().BeLessThan(WorkPack.PackBytes);
        pack.Text.Should().Contain(under.Single(file => file.Key.Contains("t4-d", StringComparison.Ordinal)).Value, "a pack under the limit holds every file whole");
    }

    [Theory]
    [InlineData("plain text\n", "```")]
    [InlineData("a ``` run\n", "````")]
    [InlineData("a ````` run of five\nand END OF PLAN abc\n```\n", "``````")]
    public void D3_EachFile_IsInAFenceOneLongerThanTheLongestBacktickRunInIt_SoItCannotEndItsOwnSection(string spec, string fence)
    {
        var files = Plan();
        files["epoch-1/e2-more/SPEC.md"] = spec;

        var text = Packed(files, "epoch-1/e2-more").Text;

        text.Should().Contain($"## {Feature}/epoch-1/e2-more/SPEC.md\n\n{fence}\n{spec}{(spec.EndsWith('\n') ? string.Empty : "\n")}{fence}\n");
    }

    [Fact]
    public void D3_ThePackText_IsTheLineNotSentThenOneSectionPerFile_EachSeparatedByABlankLine()
    {
        var pack = Packed(Plan(), "epoch-1/e2-more/stories/s1-more");

        pack.Text.Should().StartWith("**Not sent** (not reviewed, and not part of the approval): ").And.EndWith("```\n");
        pack.Text.Split("\n## ").Should().HaveCount(pack.Sections.Count + 1, "the line Not sent, then one section for each file");
        pack.Text.Should().Contain("```\n\n## plan/PLAN.md\n\n```\n# The plan\n\nWhat the feature is.\n```\n\n## plan/epoch-1/epoch.yaml\n");
    }

    [Fact]
    public void D3_TheLineNotSent_NamesTheArchitectureFilesAndTheFreeMarkdownFilesWithTheirSizesAndCountsTheNodesOutside()
    {
        var files = Plan();
        files["feature.yaml"] = files["feature.yaml"] + "architecture:\n  - architecture/design.md\n  - architecture/gone.md\n";
        files["architecture/design.md"] = "# Design of the thing\n";
        files["epoch-1/e1-core/RESULTS_one.md"] = "results\n";
        files["notes.md"] = "notes\n";

        var text = Packed(files, "epoch-1/e2-more/stories/s1-more").Text;

        var line = text.Split('\n').Single(row => row.StartsWith("**Not sent", StringComparison.Ordinal));
        line.Should().Contain("architecture/design.md (22 bytes)").And.Contain("architecture/gone.md (not in the commit)");
        line.Should().Contain("epoch-1/e1-core/RESULTS_one.md (8 bytes)").And.Contain("notes.md (6 bytes)");
        line.Should().Contain("nodes outside the scope: 5", "the epic e1-core with its story and task, and the other story of e2-more with its task; the epoch and the epic above the story are ancestors, in the pack");
        text.Should().NotContain("Design of the thing").And.NotContain("results\n");
    }

    [Fact]
    public void D4_TheApprovalLines_AreOneForEveryPackedFileAndOneForEveryOutsideDependency_InPackOrder()
    {
        var files = Plan();

        var pack = Packed(files, "epoch-1/e2-more/stories/s1-more");

        var f = pack.Approval.Where(line => line.Kind == "F").ToList();
        f.Select(line => line.Key).Should().Equal(pack.Sections.Select(section => section.Path), "one F line for every file whose text is in the pack: the scope files, feature.yaml, the plan file, the ancestors and the spec");
        f.All(line => line.Value == ListedFolder.BlobOf(files[line.Key.Substring(Feature.Length + 1)])).Should().BeTrue("the blob id of every line is the blob of its file");
        f.Select(line => line.Key).Should().Contain([$"{Feature}/feature.yaml", $"{Feature}/PLAN.md", $"{Feature}/epoch-1/epoch.yaml", $"{Feature}/epoch-1/e2-more/epic.yaml", $"{Feature}/epoch-1/e2-more/SPEC.md"]);
        pack.Approval.Select(line => line.Kind).SkipWhile(kind => kind == "F").Should().OnlyContain(kind => kind == "D", "the F lines come first, then the D lines");
        pack.Approval.Where(line => line.Kind == "D").Select(line => line.Text).Should().Equal("D epoch-1/e1-core/s1-first Story s1-first", "D epoch-1/e1-core/s1-first/t1-a Task t1-a");
        pack.ApprovalId.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(pack.Approval.Select(line => line.Text + "\n"))))));
    }

    [Theory]
    [InlineData("epoch-1/e2-more/stories/s1-more/tasks/t1-m/task.yaml", true)]
    [InlineData("epoch-1/e2-more/stories/s1-more/story.yaml", true)]
    [InlineData("feature.yaml", true)]
    [InlineData("PLAN.md", true)]
    [InlineData("epoch-1/epoch.yaml", true)]
    [InlineData("epoch-1/e2-more/epic.yaml", true)]
    [InlineData("epoch-1/e2-more/SPEC.md", true)]
    [InlineData("epoch-1/e1-core/stories/s1-first/story.yaml", true)]
    [InlineData("epoch-1/e1-core/epic.yaml", false)]
    [InlineData("epoch-1/e2-more/stories/s2-other/tasks/t1-o/task.yaml", false)]
    [InlineData("epoch-1/e2-more/stories/s2-other/story.yaml", false)]
    [InlineData("epoch-1/e1-core/SPEC.md", false)]
    [InlineData("architecture/design.md", false)]
    public void D4_TheApprovalId_ChangesWithWhatTheReviewersRead_AndOnlyWithThat(string changed, bool breaks)
    {
        var files = Plan();
        files["architecture/design.md"] = "# Design\n";
        var scope = "epoch-1/e2-more/stories/s1-more";
        var before = Packed(files, scope).ApprovalId;
        files[changed] = changed.EndsWith(".yaml", StringComparison.Ordinal)
            ? files[changed] + "# a comment\n"
            : files.GetValueOrDefault(changed, string.Empty) + "one more line\n";
        if (changed == "epoch-1/e1-core/stories/s1-first/story.yaml")
        {
            files[changed] = files[changed].Replace("Story s1-first", "Story s1-first, renamed", StringComparison.Ordinal);
        }

        var after = Packed(files, scope).ApprovalId;

        (after != before).Should().Be(breaks, changed);
    }

    [Fact]
    public void D4_ANewResultsFile_AndANewNodeNobodyInTheScopeDependsOn_DoNotChangeTheApprovalId_ButTheTitleOfADependencyDoes()
    {
        var files = Plan();
        var scope = "epoch-1/e2-more/stories/s1-more";
        var before = Packed(files, scope);

        files["epoch-1/e2-more/stories/s1-more/RESULTS_t1.md"] = "# results\n";
        files["epoch-1/e1-core/stories/s5-new/story.yaml"] = "id: s5-new\ntitle: \"New\"\ndependsOn: []\n";
        files["epoch-1/e1-core/stories/s5-new/tasks/t1-z/task.yaml"] = files["epoch-1/e2-more/stories/s2-other/tasks/t1-o/task.yaml"].Replace("t1-o", "t1-z", StringComparison.Ordinal);
        var after = Packed(files, scope);

        after.ApprovalId.Should().Be(before.ApprovalId);
        after.Text.Should().NotBe(before.Text, "the line Not sent changed: it names the new files");

        files["epoch-1/e1-core/stories/s1-first/story.yaml"] = files["epoch-1/e1-core/stories/s1-first/story.yaml"].Replace("Story s1-first", "Story first, renamed", StringComparison.Ordinal);
        Packed(files, scope).ApprovalId.Should().NotBe(before.ApprovalId, "the title of a dependency is part of what the reviewers read");
    }

    [Fact]
    public void ThePlanAndSpecNames_AreChecked_AsAPathOfTheFolder()
    {
        var files = Plan();
        files["feature.yaml"] = files["feature.yaml"].Replace("plan: PLAN.md", "plan: ../outside.md", StringComparison.Ordinal);
        Refused(files, "epoch-1/e2-more").Should().Contain("plan").And.Contain("..");

        files = Plan();
        files["epoch-1/e2-more/epic.yaml"] = files["epoch-1/e2-more/epic.yaml"].Replace("SPEC.md", "missing.md", StringComparison.Ordinal);
        Refused(files, "epoch-1/e2-more").Should().Contain("spec").And.Contain("missing.md").And.Contain("not a file of the commit");

        files = Plan();
        files["PLAN.md"] = new string('x', WorkPack.FileBytes + 1);
        Refused(files, "epoch-1/e2-more").Should().Contain("PLAN.md").And.Contain("65537");

        files = Plan();
        files["feature.yaml"] = files["feature.yaml"].Replace("plan: PLAN.md", "plan: .env", StringComparison.Ordinal);
        files[".env"] = "SECRET=1\n";
        Refused(files, "epoch-1/e2-more").Should().Contain(".env");
    }

    [Fact]
    public void NamedFiles_AreThePlanFileAndTheSpecFilesOfTheEpics_AsPathsFromTheFeatureFolder()
    {
        WorkPack.NamedFiles(new ListedFolder(Plan()), Reader).Should().Equal("PLAN.md", "epoch-1/e1-core/SPEC.md", "epoch-1/e2-more/SPEC.md");
    }

    [Fact]
    public void TheSourceLine_SaysWhoPackedWhatAtWhichCommitUnderWhichApprovalAndLimits()
    {
        var pack = Packed(Plan(), "epoch-1/e2-more");

        WorkPack.SourceLine(pack, new string('a', 40), "device").Should().Be(
            $"(packed by the server from {Feature}/epoch-1/e2-more at commit aaaaaaaaaaaa, approval {pack.ApprovalId}, {pack.Sections.Count} files, {pack.Bytes} bytes; validate_work: valid, limits device)");
    }


    [Fact]
    public void D2_AStoryScopedPack_NamesTheStoryAndTheTaskOfAnotherStoryItDependsOn_InItsText_BeforeTheFiles()
    {
        var pack = Packed(Plan(), "epoch-1/e2-more/stories/s1-more");

        var line = pack.Text.Split('\n').Single(one => one.StartsWith("**Depends on, outside the scope**", StringComparison.Ordinal));
        line.Should().Contain("epoch-1/e1-core/s1-first (\"Story s1-first\")").And.Contain("epoch-1/e1-core/s1-first/t1-a (\"Task t1-a\")", "the task of ANOTHER story that a task of this story depends on is named with its title, as the approval names it");
        pack.Text.IndexOf(line, StringComparison.Ordinal).Should().BeLessThan(pack.Text.IndexOf("## ", StringComparison.Ordinal), "the line stands before the first file section");
        pack.Bytes.Should().Be(Encoding.UTF8.GetByteCount(pack.Text));
    }

    [Fact]
    public void D2_AnEpicScopedPack_HasTheTextItHadBeforeTheLine_SoAnApprovedEpicRoundKeepsItsTextId()
    {
        var pack = Packed(Plan(), "epoch-1/e2-more");

        pack.Approval.Should().Contain(line => line.Kind == "D", "the epic depends on e1-core: the approval names it");
        pack.Text.Should().NotContain("Depends on, outside the scope", "the text of an epoch or an epic scope is unchanged, byte for byte");
    }
}
