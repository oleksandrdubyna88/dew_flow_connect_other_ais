using System.Collections.Immutable;
using System.Text;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Outlining;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The outline section of a feature review: changed members marked, the D22 member hunks attached to the
/// innermost changed member, and — whatever the input — never a byte over the budget, with every file
/// and every member hunk either shown or named.
/// </summary>
public sealed class OutlineComposerTests
{
    private const int Budget = 168 * 1024;

    private static OutlineEntry Entry(int depth, string signature, int start, int end) =>
        new(depth, "member", signature, signature, start, end);

    /// <summary>A class of lines 1–40 holding two methods, 5–15 and 20–30.</summary>
    private static readonly ImmutableArray<OutlineEntry> ClassWithTwoMethods =
    [
        Entry(0, "public sealed class Shop", 1, 40),
        Entry(1, "public void Buy(int n)", 5, 15),
        Entry(1, "public void Sell(int n)", 20, 30),
    ];

    private static OutlinedFile File(
        string path, FileChange change, int added, int deleted, ImmutableArray<OutlineEntry> entries,
        IReadOnlyList<LineSpan>? changed = null, IReadOnlyList<DiffLine>? hunks = null) =>
        new(new ChangedFile(path, change == FileChange.Renamed ? "old/" + path : string.Empty, change, added, deleted, false),
            SourceOutline.Of(OutlineLanguage.CSharp, 1000, entries),
            changed ?? [],
            hunks ?? []);

    private static DiffLine Line(int hunk, char kind, int at, string text) => new(hunk, kind, at, $"{kind}{text}");

    [Fact]
    public void AMemberIntersectingAChangedSpan_IsMarked_AndItsContainerToo_ButNotItsSibling()
    {
        var file = File("src/Shop.cs", FileChange.Modified, 1, 1, ClassWithTwoMethods, changed: [new LineSpan(8, 8)]);

        var section = OutlineComposer.Compose([file], Budget, FeatureBudget.CollapseAboveBytes).Section;

        section.Should().Contain("### src/Shop.cs (M, +1/-1)");
        section.Should().Contain("* public sealed class Shop [1-40]");
        section.Should().Contain("*   public void Buy(int n) [5-15]");
        section.Should().Contain("    public void Sell(int n) [20-30]", "an unchanged sibling is listed, not marked");
    }

    [Fact]
    public void AnAddedFile_IsMarkedNew_AndNoneOfItsMembersIsStarred()
    {
        var file = File("src/New.cs", FileChange.Added, 40, 0, ClassWithTwoMethods, changed: [new LineSpan(1, 40)]);

        var section = OutlineComposer.Compose([file], Budget, FeatureBudget.CollapseAboveBytes).Section;

        section.Should().Contain("### src/New.cs (A, new, +40/-0)");
        section.Split('\n').Where(l => l.StartsWith("* ", StringComparison.Ordinal)).Should().BeEmpty();
    }

    [Fact]
    public void ARename_NamesWhereTheFileCameFrom()
    {
        var file = File("src/Shop.cs", FileChange.Renamed, 1, 0, ClassWithTwoMethods);

        OutlineComposer.Compose([file], Budget, FeatureBudget.CollapseAboveBytes).Section
            .Should().Contain("### src/Shop.cs (R, +1/-0) — renamed from old/src/Shop.cs");
    }

    [Fact]
    public void AHunkInsideAMethod_IsShownUnderTheMethod_NotUnderItsClass()
    {
        var file = File("src/Shop.cs", FileChange.Modified, 1, 1, ClassWithTwoMethods, changed: [new LineSpan(9, 9)], hunks:
        [
            Line(0, ' ', 6, "    var a = 1;"), Line(0, '-', 9, "    var total = n;"), Line(0, '+', 9, "    var total = n * 2;"), Line(0, ' ', 10, "    return;"),
        ]);

        var composed = OutlineComposer.Compose([file], Budget, FeatureBudget.CollapseAboveBytes);

        composed.Section.Should().Contain("#### src/Shop.cs — public void Buy(int n) [5-15] (+1/-1)");
        composed.Section.Should().NotContain("#### src/Shop.cs — public sealed class Shop", "the class holds the method; the innermost changed member owns the line");
        composed.Section.Should().Contain("@@ +6 @@\n     var a = 1;\n-    var total = n;\n+    var total = n * 2;\n     return;");
    }

    [Fact]
    public void LinesOutsideEveryChangedMember_AreNotShown_AndAMemberWithOnlyContext_IsNoUnit()
    {
        var file = File("src/Shop.cs", FileChange.Modified, 1, 0, ClassWithTwoMethods, changed: [new LineSpan(42, 42)], hunks:
        [
            Line(0, ' ', 39, "  }"), Line(0, ' ', 40, "}"), Line(0, '+', 42, "using Extra;"),
        ]);

        var composed = OutlineComposer.Compose([file], Budget, FeatureBudget.CollapseAboveBytes);

        composed.Section.Should().NotContain(MemberHunks.Heading, "no changed member holds a changed line");
        composed.CutHunks.Should().BeEmpty();
    }

    [Fact]
    public void AnAddedFile_ContributesEveryMember()
    {
        var file = File("src/New.cs", FileChange.Added, 3, 0, ClassWithTwoMethods, hunks:
        [
            Line(0, '+', 2, "  int field;"), Line(0, '+', 6, "    Buy();"), Line(0, '+', 21, "    Sell();"),
        ]);

        var units = MemberHunks.Units(file);

        units.Select(u => u.Member.Signature).Should().BeEquivalentTo(["public sealed class Shop", "public void Buy(int n)", "public void Sell(int n)"]);
    }

    /// <summary>
    /// The room goes to the many small edits first: the feature-pack trial's arm F found 0 of the 4 planted
    /// defects whose member the hunk budget cut, and on ts2 every unit cut had 13 changed lines or fewer —
    /// a smallest-first CUT removes exactly the body edits a defect is made of.
    /// </summary>
    [Fact]
    public void WhenTheHunksDoNotFit_TheLargestChangesAreCut_AndEveryCutIsNamed()
    {
        var big = UnitsFile("src/Big.cs", 60);
        var small = UnitsFile("src/Small.cs", 2);
        var whole = OutlineComposer.Compose([big, small], Budget, FeatureBudget.CollapseAboveBytes).Section;
        var smallBytes = Encoding.UTF8.GetByteCount(MemberHunks.Units(small).Single().Text);

        // Room for the big unit alone, or the small one alone — never both. Taken largest first, the big
        // one would fit and push the small one out; this budget is what tells the two orders apart.
        var budget = Encoding.UTF8.GetByteCount(whole) - (smallBytes / 2) + 100;

        var composed = OutlineComposer.Compose([big, small], budget, FeatureBudget.CollapseAboveBytes);

        Encoding.UTF8.GetByteCount(composed.Section).Should().BeLessThanOrEqualTo(budget);
        composed.Section.Should().Contain("#### src/Small.cs", "the small edit is kept");
        composed.Section.Should().NotContain("#### src/Big.cs", "the largest change is the one cut");
        composed.CutHunks.Should().ContainSingle().Which.Should().Be(new CutHunk("src/Big.cs", 5, 75, 60, 0));
        composed.Section.Should().Contain("1 further member hunk(s) did not fit the budget");
    }

    /// <summary>
    /// The trial's tsx2, reproduced: a few enormous members used to fill the whole hunk room — six units took
    /// all 62 KB while 664 members of 20 changed lines or fewer were cut. Capped per member and filled
    /// smallest first, every small edit keeps its hunk and an enormous one is still SHOWN, truncated.
    /// </summary>
    [Fact]
    public void AFewEnormousMembers_NoLongerStarveTheManySmallOnes()
    {
        var enormous = Enumerable.Range(1, 3).Select(i => UnitsFile($"src/Enormous{i}.cs", 400)).ToList();
        var small = Enumerable.Range(1, 30).Select(i => UnitsFile($"src/Small{i:00}.cs", 12)).ToList();
        var files = enormous.Concat(small).ToList();
        var whole = OutlineComposer.Compose(files, 4 * Budget, FeatureBudget.CollapseAboveBytes).Section;
        var enormousBytes = Encoding.UTF8.GetByteCount(MemberHunks.Units(enormous[0]).Single().Text) + 1;
        var budget = Encoding.UTF8.GetByteCount(whole) - (2 * enormousBytes) + 150;

        var composed = OutlineComposer.Compose(files, budget, FeatureBudget.CollapseAboveBytes);

        Encoding.UTF8.GetByteCount(composed.Section).Should().BeLessThanOrEqualTo(budget);
        foreach (var file in small)
        {
            composed.Section.Should().Contain($"#### {file.File.Path} — ", "every small edit keeps its hunk");
        }

        composed.CutHunks.Select(c => c.Path).Should().OnlyContain(p => p.StartsWith("src/Enormous", StringComparison.Ordinal));
        composed.Section.Should().Contain("more changed lines — ask for source]", "an enormous member is shown truncated, not dropped");
    }

    [Fact]
    public void AMemberLongerThanTheCap_IsTruncated_SayingHowManyChangedLinesItLeftOut()
    {
        var unit = MemberHunks.Units(UnitsFile("src/Enormous.cs", 400)).Single();

        Encoding.UTF8.GetByteCount(unit.Text).Should().BeLessThanOrEqualTo(FeatureBudget.MaxHunkBytesPerMember);
        unit.Added.Should().Be(400, "the header still counts the whole change");
        var shown = unit.Text.Split('\n').Count(l => l.StartsWith("+    var line", StringComparison.Ordinal));
        unit.Text.Should().Contain($"[{400 - shown} more changed lines — ask for source]");
        unit.Text.TrimEnd('\n').Should().EndWith("```", "the fence still closes after the marker");
    }

    [Fact]
    public void AMemberWithinTheCap_IsShownWhole() =>
        MemberHunks.Units(UnitsFile("src/Small.cs", 12)).Single().Text.Should().NotContain("more changed lines");

    [Fact]
    public void AnOversizedOutline_CollapsesUnchangedMembersFirst_ThenDropsTheSmallestChange()
    {
        var wide = WideFile("src/Wide.cs", 200, 120, changedLine: 15);
        var tiny = File("src/Tiny.cs", FileChange.Modified, 1, 0, ClassWithTwoMethods, changed: [new LineSpan(8, 8)]);
        var whole = OutlineComposer.Compose([wide, tiny], Budget, FeatureBudget.CollapseAboveBytes);
        var budget = Encoding.UTF8.GetByteCount(whole.Section) / 3;

        var composed = OutlineComposer.Compose([wide, tiny], budget, FeatureBudget.CollapseAboveBytes);

        Encoding.UTF8.GetByteCount(composed.Section).Should().BeLessThanOrEqualTo(budget);
        composed.Collapsed.Should().ContainSingle().Which.Path.Should().Be("src/Wide.cs");
        composed.Section.Should().Contain("unchanged members elided");
        composed.Section.Should().Contain("*   public void Member4() [15-17]", "a changed member survives the collapse");
        composed.Section.Should().Contain("* public sealed class Wide", "and so does the path down to it");
    }

    [Fact]
    public void FilesThatStillDoNotFit_AreDroppedSmallestChangeFirst_AndNamed()
    {
        var files = Enumerable.Range(1, 6).Select(i => File($"src/F{i}.cs", FileChange.Modified, i * 10, 0, ClassWithTwoMethods, changed: [new LineSpan(8, 8)])).ToList();
        var section = OutlineComposer.Compose(files, Budget, FeatureBudget.CollapseAboveBytes).Section;
        var budget = Encoding.UTF8.GetByteCount(section[..section.IndexOf("### src/F3.cs", StringComparison.Ordinal)]);

        var composed = OutlineComposer.Compose(files, budget, FeatureBudget.CollapseAboveBytes);

        composed.Dropped.Should().Equal("src/F1.cs", "src/F2.cs", "src/F3.cs");
        composed.Outlined.Should().Be(3);
    }

    /// <summary>
    /// The property: for any mix of files, marks and hunks, the section never passes its budget, and every
    /// file is outlined or named as dropped, and every changed member's hunk is shown or named as cut.
    /// </summary>
    [Fact]
    public void ForAnyInput_TheBudgetIsNeverExceeded_AndNothingIsLostUnnamed()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var random = new Random(seed);
            var files = Enumerable.Range(0, random.Next(0, 25)).Select(i => RandomFile(random, i)).ToList();
            var budget = random.Next(0, 40 * 1024);

            var composed = OutlineComposer.Compose(files, budget, FeatureBudget.CollapseAboveBytes);
            var again = OutlineComposer.Compose([.. files.AsEnumerable().Reverse()], budget, FeatureBudget.CollapseAboveBytes);

            Encoding.UTF8.GetByteCount(composed.Section).Should().BeLessThanOrEqualTo(budget, $"seed {seed}");
            again.Should().BeEquivalentTo(composed, $"seed {seed}: the order files arrive in changes nothing");
            foreach (var file in files)
            {
                (composed.Section.Contains($"### {file.File.Path} (", StringComparison.Ordinal) || composed.Dropped.Contains(file.File.Path))
                    .Should().BeTrue($"seed {seed}: {file.File.Path} is outlined or named");
            }

            // A LAMBDA, not the method group: `SelectMany(MemberHunks.Units)` binds to the overload that
            // passes the element's INDEX, which lands in `Units`' optional `cap` — so the first version of
            // this check built every unit truncated to its index in bytes, and its byte counts meant nothing.
            var units = files.SelectMany(f => MemberHunks.Units(f)).ToList();
            foreach (var unit in units)
            {
                (Shows(composed.Section, unit) || composed.CutHunks.Contains(unit.AsCut))
                    .Should().BeTrue($"seed {seed}: the hunk of {unit.Path} {unit.Member.Signature} is shown or named");
            }

            TheHunksGetTheirReserve(composed.Section, units, budget, seed);
        }
    }

    /// <summary>
    /// The reserve, as a property: when changed members exist, the hunks shown take at least as much as the
    /// reserve (less the section's own heading and legend) or as much as all of them need — whichever is
    /// smaller — give or take the one unit whose size stopped the smallest-first fill.
    /// </summary>
    private static void TheHunksGetTheirReserve(string section, IReadOnlyList<MemberUnit> units, int budget, int seed)
    {
        if (units.Count == 0)
        {
            return;
        }

        const int SectionOverhead = 2 * 1024;
        var needed = units.Sum(UnitBytes);
        var shown = units.Where(u => Shows(section, u)).Sum(UnitBytes);
        var floor = Math.Min(FeatureBudget.HunkReserveFor(budget) - SectionOverhead, needed) - units.Max(UnitBytes);

        shown.Should().BeGreaterThanOrEqualTo(floor, $"seed {seed}: the hunks get their reserve before the outline is cut");
    }

    private static bool Shows(string section, MemberUnit unit) =>
        section.Contains($"#### {unit.Path} — {unit.Member.Signature} [{unit.Member.StartLine}-", StringComparison.Ordinal);

    private static int UnitBytes(MemberUnit unit) => Encoding.UTF8.GetByteCount(unit.Text) + 1;

    /// <summary>
    /// The S8 notices, reproduced: an outline that alone overfills the budget (322 KB against 168 KB,
    /// S0.2) and hundreds of small changed members. Before the hunk reserve the outline took the whole
    /// budget and NO hunk fitted — the hybrid collapsed back to the outline-only arm the feature-pack
    /// trial found worse. With the reserve the hunks are placed first, the smallest change first, and the
    /// outline takes what is left — and the section still never passes its budget.
    /// </summary>
    [Fact]
    public void ANoticesShapedFeature_WhoseOutlineAloneFillsTheBudget_StillShowsItsHunks()
    {
        var files = Enumerable.Range(0, 220).Select(NoticesFile).ToList();
        var unbounded = OutlineComposer.Compose(files, 20 * Budget, FeatureBudget.CollapseAboveBytes).Section;
        var outlineAlone = Encoding.UTF8.GetByteCount(unbounded[..unbounded.IndexOf(MemberHunks.Heading, StringComparison.Ordinal)]);
        outlineAlone.Should().BeGreaterThan(Budget, "the fixture must be notices-shaped: its outline alone overfills the budget");

        var composed = OutlineComposer.Compose(files, Budget, FeatureBudget.CollapseAboveBytes);

        Encoding.UTF8.GetByteCount(composed.Section).Should().BeLessThanOrEqualTo(Budget);
        var shown = composed.Section.Split('\n').Count(l => l.StartsWith("#### ", StringComparison.Ordinal));
        shown.Should().BeGreaterThanOrEqualTo(20, "the reserve gives the small changes their hunks even when the outline alone is over");
        HunkBytes(composed.Section).Should().BeGreaterThanOrEqualTo(
            (56 * 1024) - (4 * 1024), "the hunks fill the 56 KB reserve, give or take the unit that did not fit");
        composed.Dropped.Should().NotBeEmpty("the outline takes the remainder — files drop, and are named");
        composed.Outlined.Should().BeGreaterThan(0, "and the outline is still there, beside the hunks");
    }

    /// <summary>When the hunks need less than the reserve, the unused part flows back to the outline.</summary>
    [Fact]
    public void AnUnusedReserve_FlowsBackToTheOutline()
    {
        var files = Enumerable.Range(0, 220).Select(i => i < 3 ? NoticesFile(i) : NoticesFile(i) with { Hunks = [] }).ToList();

        var composed = OutlineComposer.Compose(files, Budget, FeatureBudget.CollapseAboveBytes);

        Encoding.UTF8.GetByteCount(composed.Section).Should().BeLessThanOrEqualTo(Budget);
        composed.Section.Split('\n').Count(l => l.StartsWith("#### ", StringComparison.Ordinal)).Should().Be(3);
        Encoding.UTF8.GetByteCount(composed.Section).Should().BeGreaterThan(
            Budget - (4 * 1024), "three small hunks take a few KB; the outline has the rest of the budget, not two thirds of it");
    }

    /// <summary>The bytes of the hunk section — from its heading to the end.</summary>
    private static int HunkBytes(string section)
    {
        var at = section.IndexOf(MemberHunks.Heading, StringComparison.Ordinal);

        return at < 0 ? 0 : Encoding.UTF8.GetByteCount(section[at..]);
    }

    /// <summary>One file of the notices shape: ~1.7 KB of outline over 30 members, one of them changed in five lines.</summary>
    private static OutlinedFile NoticesFile(int index)
    {
        var entries = ImmutableArray.CreateRange(new[] { Entry(0, $"public sealed class Notice{index:000}", 1, 200) }
            .Concat(Enumerable.Range(0, 30).Select(i => Entry(1, $"public string Member{i:00}(string notice, int attempt)", 3 + (i * 6), 8 + (i * 6)))));
        var hunks = Enumerable.Range(0, 5).Select(i => Line(0, '+', 10 + i, $"        var step{i} = notice.Length + attempt;")).ToList();

        return File($"src/notices/Notice{index:000}.cs", FileChange.Modified, 5, 0, entries, changed: [new LineSpan(10, 14)], hunks: hunks);
    }

    /// <summary>A class with one method whose every line changed — a member hunk of a chosen size.</summary>
    private static OutlinedFile UnitsFile(string path, int changedLines)
    {
        var entries = ImmutableArray.Create(Entry(0, $"public sealed class {System.IO.Path.GetFileNameWithoutExtension(path)}", 1, 20 + changedLines), Entry(1, "public void Buy(int n)", 5, 15 + changedLines));
        var hunks = Enumerable.Range(0, changedLines).Select(i => Line(0, '+', 6 + i, $"    var line{i} = {i}; // padding padding padding")).ToList();

        return File(path, FileChange.Modified, changedLines, 0, entries, changed: [new LineSpan(6, 6 + changedLines)], hunks: hunks);
    }

    private static OutlinedFile WideFile(string path, int members, int added, int changedLine)
    {
        var entries = ImmutableArray.CreateRange(new[] { Entry(0, $"public sealed class {System.IO.Path.GetFileNameWithoutExtension(path)}", 1, (members * 3) + 2) }
            .Concat(Enumerable.Range(0, members).Select(i => Entry(1, $"public void Member{i}()", 3 + (i * 3), 5 + (i * 3)))));

        return File(path, FileChange.Modified, added, 0, entries, changed: [new LineSpan(changedLine, changedLine)]);
    }

    private static OutlinedFile RandomFile(Random random, int index)
    {
        var members = random.Next(0, 60);
        var entries = ImmutableArray.CreateRange(Enumerable.Range(0, members)
            .Select(i => Entry(i % 3 == 0 ? 0 : 1, $"public void M{i}_{new string('x', random.Next(0, 80))}()", (i * 5) + 1, (i * 5) + 4)));
        var spans = Enumerable.Range(0, random.Next(0, 6)).Select(_ => random.Next(1, (members * 5) + 2)).Select(s => new LineSpan(s, s + random.Next(0, 3))).ToList();
        var hunks = spans.SelectMany((s, h) => Enumerable.Range(s.Start, s.End - s.Start + 1).Select(at => Line(h, random.Next(3) == 0 ? '-' : '+', at, new string('y', random.Next(0, 120))))).ToList();
        var change = random.Next(4) switch { 0 => FileChange.Added, 1 => FileChange.Renamed, _ => FileChange.Modified };

        return File($"src/dir{random.Next(4)}/F{index}.cs", change, random.Next(0, 500), random.Next(0, 500), entries, spans, hunks);
    }
}
