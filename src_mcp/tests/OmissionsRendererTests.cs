using System.Text;
using CoaiMcp.Core.Feature;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// "Files not outlined" and "What this context left out" are never cut: they fit the reserve by being
/// said more briefly, and every omitted file stays named — as itself, or inside a named directory.
/// </summary>
public sealed class OmissionsRendererTests
{
    private static readonly IReadOnlyList<string> NoContext = [];

    [Fact]
    public void AtFullDetail_EachFileCarriesItsSizeLinesAndReason()
    {
        var omissions = FeatureOmissions.None with
        {
            NotOutlined =
            [
                new NotOutlined("assets/logo.png", "binary; not read", 2048),
                new NotOutlined("src/Gone.cs", "deleted at head"),
                new NotOutlined("src/Broken.cs", "unsupported (parse failed): 40 % of its text did not parse", 900, 31),
            ],
        };

        var text = OmissionsRenderer.Render(omissions, NoContext, FeatureBudget.OmissionsReserveBytes);

        text.Should().Contain("- assets/logo.png — 2048 bytes — binary; not read");
        text.Should().Contain("- src/Gone.cs — deleted at head", "a size nobody measured is not printed as zero");
        text.Should().Contain("- src/Broken.cs — 900 bytes, 31 lines — unsupported (parse failed)");
        text.Should().Contain(OmissionsRenderer.NothingLeftOut);
    }

    [Fact]
    public void CutHunksCollapsedAndDroppedFiles_AreAllNamed()
    {
        var omissions = new FeatureOmissions(
            [], [new CollapsedFile("src/Panel.cs", 41)], ["src/Small.cs"], [new CutHunk("src/A.cs", 5, 17, 2, 0), new CutHunk("src/A.cs", 30, 40, 1, 1)], ["a note"]);

        var text = OmissionsRenderer.Render(omissions, ["- The plan was cut at 64 KB."], FeatureBudget.OmissionsReserveBytes);

        text.Should().Contain("- The plan was cut at 64 KB.");
        text.Should().Contain("Unchanged members collapsed in 1 large file(s): src/Panel.cs (41)");
        text.Should().Contain("Elided — ask for source by name (1 file(s), smallest change first): src/Small.cs");
        text.Should().Contain("Member hunks cut for the outline budget (2 member(s) in 1 file(s)").And.Contain("src/A.cs: [5-17] +2/-0, [30-40] +1/-1");
        text.Should().Contain("- a note");
        text.Should().NotContain(OmissionsRenderer.NothingLeftOut);
    }

    [Fact]
    public void AListTooLongForTheReserve_IsSaidMoreBriefly_NeverCut()
    {
        var files = Enumerable.Range(0, 600).Select(i => new NotOutlined($"src/area{i % 7}/deep/File{i}.txt", "unsupported (language); not read", 1000 + i)).ToList();
        var omissions = FeatureOmissions.None with { NotOutlined = files };

        var text = OmissionsRenderer.Render(omissions, NoContext, FeatureBudget.OmissionsReserveBytes);

        Encoding.UTF8.GetByteCount(text).Should().BeLessThanOrEqualTo(FeatureBudget.OmissionsReserveBytes);
        Encoding.UTF8.GetByteCount(OmissionsRenderer.Render(omissions, NoContext, OmissionsRenderer.Detail.Full))
            .Should().BeGreaterThan(FeatureBudget.OmissionsReserveBytes, "the fixture has to be too long to be worth anything");
        AllNamed(text, files.Select(f => f.Path)).Should().BeTrue("every file is named, as itself or inside a named directory");
    }

    /// <summary>The property: for any omissions, the two sections fit the reserve and name every path — unless even counts are all that fits, and then they say so.</summary>
    [Fact]
    public void ForAnyOmissions_TheReserveIsNeverExceeded_AndEveryPathIsAccountedFor()
    {
        for (var seed = 1; seed <= 300; seed++)
        {
            var random = new Random(seed);
            var omissions = RandomOmissions(random);
            var context = Enumerable.Range(0, random.Next(0, 4)).Select(i => $"- context line {i}").ToList();

            var text = OmissionsRenderer.Render(omissions, context, FeatureBudget.OmissionsReserveBytes);

            Encoding.UTF8.GetByteCount(text).Should().BeLessThanOrEqualTo(FeatureBudget.OmissionsReserveBytes, $"seed {seed}");
            context.All(c => text.Contains(c, StringComparison.Ordinal)).Should().BeTrue($"seed {seed}: the context's own lines are kept verbatim");
            var paths = omissions.NotOutlined.Select(n => n.Path).Concat(omissions.Dropped).Concat(omissions.CutHunks.Select(c => c.Path)).Concat(omissions.Collapsed.Select(c => c.Path));
            (AllNamed(text, paths) || text.Contains("too many to name inside the", StringComparison.Ordinal))
                .Should().BeTrue($"seed {seed}: every omitted path is named, or the text says it could only count them");
        }
    }

    [Fact]
    public void EveryLevel_ButCounts_NamesEveryPath()
    {
        var omissions = RandomOmissions(new Random(7));
        var paths = omissions.NotOutlined.Select(n => n.Path).Concat(omissions.Dropped).Concat(omissions.CutHunks.Select(c => c.Path)).ToList();

        foreach (var detail in Enum.GetValues<OmissionsRenderer.Detail>().Where(d => d != OmissionsRenderer.Detail.Counts))
        {
            AllNamed(OmissionsRenderer.Render(omissions, NoContext, detail), paths).Should().BeTrue($"{detail} names every path");
        }
    }

    private static bool AllNamed(string text, IEnumerable<string> paths) =>
        paths.All(p => text.Contains(p, StringComparison.Ordinal) || Folders(p).Any(f => text.Contains($"{f}/** (", StringComparison.Ordinal)));

    private static IEnumerable<string> Folders(string path)
    {
        var parts = path.Split('/');
        for (var depth = 1; depth < parts.Length; depth++)
        {
            yield return string.Join('/', parts.Take(depth));
        }
    }

    private static FeatureOmissions RandomOmissions(Random random)
    {
        string Path(int i) => string.Join('/', Enumerable.Range(0, random.Next(0, 5)).Select(d => $"d{random.Next(6)}")) is { Length: > 0 } dir ? $"{dir}/f{i}.x" : $"f{i}.x";
        string[] reasons = ["binary; not read", "unsupported (language); not read", "deleted at head", "past the 400-file outline cap; not read"];

        return new FeatureOmissions(
            [.. Enumerable.Range(0, random.Next(0, 900)).Select(i => new NotOutlined(Path(i), reasons[random.Next(reasons.Length)], random.Next(-1, 100000), random.Next(-1, 3000)))],
            [.. Enumerable.Range(0, random.Next(0, 20)).Select(i => new CollapsedFile(Path(1000 + i), random.Next(1, 90)))],
            [.. Enumerable.Range(0, random.Next(0, 200)).Select(i => Path(2000 + i))],
            [.. Enumerable.Range(0, random.Next(0, 300)).Select(i => new CutHunk(Path(3000 + (i / 3)), i * 10, (i * 10) + 5, random.Next(0, 50), random.Next(0, 50)))],
            [.. Enumerable.Range(0, random.Next(0, 2)).Select(i => $"note {i}")]);
    }
}
