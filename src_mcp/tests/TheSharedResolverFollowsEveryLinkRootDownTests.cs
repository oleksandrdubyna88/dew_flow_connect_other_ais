using System.Diagnostics;
using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <see cref="RealPath"/> is <c>realpath(3)</c>: root-down, a stack of pending segments, a link's RAW text pushed in front of
/// the rest, one budget of 40 link expansions, and a failure — never a guess — for everything it cannot follow to the end.
/// </summary>
/// <remarks>
/// The behaviour at the three sites that use it is pinned in <c>ALinkCannotCarryTheReaderOutsideTests</c>,
/// <c>AWorktreeIsOursOnlyByItsRealPathTests</c> and <c>AWorktreePathIsComparedByItsRealPathTests</c>; this file is the resolver
/// itself, on real links and — for the Windows shapes no Linux disk can make — on a stood-in entry reader.
/// </remarks>
public sealed class TheSharedResolverFollowsEveryLinkRootDownTests : IDisposable
{
    private readonly TempDir _work = TempDir.For("testsToDelete-realpath-unit-");
    private readonly string _real;

    public TheSharedResolverFollowsEveryLinkRootDownTests()
    {
        _real = RealPath.Resolve(_work.Path) is { Ok: true } real ? real.Path : _work.Path;
        Directory.CreateDirectory(Path.Combine(_real, "a", "b"));
        File.WriteAllText(Path.Combine(_real, "a", "b", "f.txt"), "f");
    }

    public void Dispose() => _work.Dispose();

    private string At(params string[] parts) => Path.Combine([_real, .. parts]);

    private static void Link(string link, string text)
    {
        try
        {
            File.CreateSymbolicLink(link, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this machine cannot create a symbolic link: {e.Message}");
        }
    }

    [Fact]
    public void ADotDotAfterALink_GoesToTheParentOfTheRealDirectory()
    {
        Link(At("l"), At("a", "b"));

        RealPath.Resolve(At("l", "..", "b", "f.txt")).Path.Should().Be(At("a", "b", "f.txt"), "l is a/b, so `l/..` is `a`");
    }

    [Fact]
    public void ARelativeLinkTargetContinuesFromTheLinksOwnDirectory()
    {
        Link(At("a", "rel"), Path.Combine("b", "f.txt"));

        RealPath.Resolve(At("a", "rel")).Path.Should().Be(At("a", "b", "f.txt"));
    }

    [Fact]
    public void ADotDotAtTheRoot_NeverClimbsAboveIt()
    {
        var root = Path.GetPathRoot(_real)!;

        RealPath.Resolve(Path.Combine(root, "..", "..")).Path.Should().Be(root);
    }

    [Fact]
    public void ADotDotAfterAMissingName_Fails_NeverAPop()
    {
        var missing = RealPath.Resolve(At("nonexistent", "..", "a"));

        missing.Ok.Should().BeFalse("the disk answers ENOENT for `nonexistent/..`; a pop would answer the folder that holds it");
        missing.Why.Should().Contain("'..'");
    }

    [Fact]
    public void AMissingName_KeepsTheRestAsWritten_InOneJoin()
    {
        RealPath.Resolve(At("a", "new", "x", ".", "y.txt")).Path.Should().Be(At("a", "new", "x", "y.txt"));
    }

    [Fact]
    public void ADanglingLink_IsALinkNotAMissingEntry()
    {
        Link(At("dangling"), At("a", "nowhere"));

        RealPath.Lstat(At("dangling")).Kind.Should().Be(EntryKind.Link);
        RealPath.Lstat(At("a", "nowhere")).Kind.Should().Be(EntryKind.Missing);
        RealPath.Resolve(At("dangling", "x")).Path.Should().Be(At("a", "nowhere", "x"), "its target is walked like any other");
    }

    [Fact]
    public void ADanglingLinkWhoseTargetIsALaterLinkOutside_IsWalkedNotTakenVerbatim()
    {
        // dangling -> a/gone ; a/gone is later made a link to outside: the resolution is of what is on the disk NOW, hop by hop.
        var outside = Directory.CreateDirectory(At("outside")).FullName;
        Link(At("dangling"), At("a", "gone"));
        Link(At("a", "gone"), outside);

        RealPath.Resolve(At("dangling", "q")).Path.Should().Be(Path.Combine(outside, "q"));
    }

    [Fact]
    public void ALoop_FailsPromptly()
    {
        Link(At("x"), At("y"));
        Link(At("y"), At("x"));
        var clock = Stopwatch.StartNew();

        var result = RealPath.Resolve(At("x"));

        result.Ok.Should().BeFalse();
        result.Why.Should().Contain("40");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(41, false)]
    public void TheBudgetIsFortyExpansions_ForTheWholeResolution(int hops, bool resolves)
    {
        for (var i = 1; i < hops; i++)
        {
            Link(At($"c{hops}-{i}"), $"c{hops}-{i + 1}");
        }

        Link(At($"c{hops}-{hops}"), At("a", "b"));

        RealPath.Resolve(At($"c{hops}-1", "f.txt")).Ok.Should().Be(resolves);
    }

    [Fact]
    public void TheBudgetIsOneCumulativeCount_NotOnePerChain()
    {
        // two chains of 21 in ONE path: 42 expansions in all.
        for (var i = 1; i < 21; i++)
        {
            Link(At($"p{i}"), $"p{i + 1}");
            Link(At("a", $"q{i}"), $"q{i + 1}");
        }

        Link(At("p21"), At("a"));
        Link(At("a", "q21"), At("a", "b"));

        RealPath.Resolve(At("p1", "q1", "f.txt")).Ok.Should().BeFalse();
    }

    [Fact]
    public void ASixtyFolderPath_CostsNoBudget()
    {
        var deep = At("deep");
        for (var i = 0; i < 60; i++)
        {
            deep = Path.Combine(deep, $"d{i}");
        }

        Directory.CreateDirectory(deep);

        RealPath.Resolve(Path.Combine(deep, "x.txt")).Path.Should().Be(Path.Combine(deep, "x.txt"));
    }

    [Fact]
    public void ANulIsARefusal_NotAnUnhandledArgumentException()
    {
        RealPath.Resolve(At("a", "b\0c")).Ok.Should().BeFalse();
        RealPath.Resolve(string.Empty).Ok.Should().BeFalse();
    }

    [Fact]
    public void ALinkTextWithANul_IsARefusal()
    {
        RealPath.Resolve(At("a"), read: p => p == At("a") ? Entry.Link("x\0y") : RealPath.Lstat(p)).Ok.Should().BeFalse();
    }

    [Fact]
    public void APathOver32KiB_IsRefusedAtOnce_NotWalkedQuadratically()
    {
        var long200k = At("a") + string.Concat(Enumerable.Repeat("/x", 200_000));
        var clock = Stopwatch.StartNew();

        var result = RealPath.Resolve(long200k);

        result.Ok.Should().BeFalse();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        result.Why.Length.Should().BeLessThan(600, "a long path is cut when a sentence echoes it");
    }

    [Fact]
    public void AResolvedPathOver32KiB_IsRefused()
    {
        // Short inputs, a long result: two links of 20,000 characters each (no disk makes a link that long — PATH_MAX — so the entry reader stands them in).
        var piece = new string('n', 20_000);
        Entry Read(string p) => Path.GetFileName(p) is "big1" or "big2" ? Entry.Link(piece) : RealPath.Lstat(p);

        RealPath.Resolve(At("a", "big1", "big2"), read: Read).Ok.Should().BeFalse();
    }

    [Fact]
    public void ANameBelowAPlainFile_Fails_BecauseTheDiskAnswersNotADirectory()
    {
        // ENOTDIR: the parent is a FILE. Nothing can be below it, so a path that goes there is no path — and it is not "missing".
        var result = RealPath.Resolve(At("a", "b", "f.txt", "zz"));

        result.Ok.Should().BeFalse();
        result.Why.Should().Contain("file");
    }

    [Fact]
    public void ADotDotAfterAPlainFile_Fails_NeverAPopToItsNeighbour()
    {
        File.WriteAllText(At("a", "b", "g.txt"), "g");

        var result = RealPath.Resolve(At("a", "b", "f.txt", "..", "g.txt"));

        result.Ok.Should().BeFalse("`f.txt/..` is ENOTDIR on the disk; a pop would read g.txt, a file nobody asked for");
    }

    [Fact]
    public void ADotDotAfterALinkToAFile_Fails_Too()
    {
        Link(At("tofile"), At("a", "b", "f.txt"));

        RealPath.Resolve(At("tofile", "..", "g.txt")).Ok.Should().BeFalse();
    }

    [Fact]
    public void ALinkToAFileThenDotDotX_FailsAsGoingBelowAFile()
    {
        // Pins a gate claim that was REJECTED: that `link-to-file/../x` slips through. The walk refuses it already, in these words.
        Link(At("link-to-file"), At("a", "b", "f.txt"));

        var result = RealPath.Resolve(At("link-to-file", "..", "x"));

        result.Ok.Should().BeFalse();
        result.Why.Should().Contain("goes below a file");
    }

    [Fact]
    public void AFileAsTheLastName_IsStillResolved()
    {
        RealPath.Resolve(At("a", "b", "f.txt")).Path.Should().Be(At("a", "b", "f.txt"));
        RealPath.Resolve(At("a", "b", "f.txt") + Path.DirectorySeparatorChar).Ok.Should().BeTrue("a trailing separator names nothing below the file");
    }

    [Fact]
    public void ResolveParent_OfNull_IsAFailureNotANullReference()
    {
        var result = RealPath.ResolveParent(null!);

        result.Ok.Should().BeFalse();
    }

    [Fact]
    public void ResolveParent_OfAPathThatIsOnlyARoot_IsTheRoot_OnUnix()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the Windows roots are in the tests below");

        RealPath.ResolveParent("/").Path.Should().Be("/");
        RealPath.ResolveParent("//").Path.Should().Be("/");
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"\\server\share\")]
    [InlineData(@"\\server\share")]
    public void ResolveParent_OfAPathThatIsOnlyARoot_IsTheRoot_OnWindows(string root)
    {
        // `C:\` trims to `C:`, which was split into the leaf `C:` and the parent `.` — the process folder — and answered `<cwd>\C:`.
        // RED only on Windows: the Unix root `/` trims to nothing and never met the split.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a drive and a UNC share are Windows roots; the same code on Unix has no such spelling");
        var expected = RealPath.Resolve(root);

        var parent = RealPath.ResolveParent(root);

        parent.Path.Should().Be(expected.Path, "a root has no parent folder to split from");
        parent.Ok.Should().BeTrue();
    }

    [Fact]
    public void ResolveParent_WhenTheParentCannotBeResolved_NamesThePathTheCallerGave()
    {
        Link(At("x"), At("y"));
        Link(At("y"), At("x"));
        var given = At("x", "leaf");

        var result = RealPath.ResolveParent(given);

        result.Ok.Should().BeFalse();
        result.Why.Should().Contain(given, "the sentence is about the path that was asked for, not about the folder part of it");
    }

    [Fact]
    public void ResolveParent_CountsTheLeafAgainstTheLengthCap()
    {
        var result = RealPath.ResolveParent(At("a") + Path.DirectorySeparatorChar + new string('x', RealPath.MaxPathChars + 10));

        result.Ok.Should().BeFalse();
    }

    [Fact]
    public void AnIoErrorIsAFailure_NotTheSpelling()
    {
        var result = RealPath.Resolve(At("a", "b"), read: _ => throw new IOException("the disk would not say"));

        result.Ok.Should().BeFalse();
        result.Why.Should().Contain("the disk would not say");
    }

    [Fact]
    public void AControlCharacterInAPathIsMadeVisibleWhereASentenceEchoesIt()
    {
        RealPath.Shown("a\u001b[31mb").Should().Be("a?[31mb");
        RealPath.Shown(new string('p', 1000)).Length.Should().BeLessThan(300);
    }

    [Fact]
    public void ResolveParent_DoesNotFollowTheLastName()
    {
        Link(At("a", "leaf"), At("a", "b"));

        RealPath.ResolveParent(At("a", "leaf")).Path.Should().Be(At("a", "leaf"));
        RealPath.Resolve(At("a", "leaf")).Path.Should().Be(At("a", "b"));
    }

    [Theory]
    [InlineData("/r/repo", "/r/repo", true)]
    [InlineData("/r/repo", "/r/repo/x/y", true)]
    [InlineData("/r/repo", "/r/repo-secrets/x", false)]
    [InlineData("/r/repo/", "/r/repo/x", true)]
    [InlineData("/r/repo", "/r/other", false)]
    public void Containment_IsTheRootPlusASeparator_NeverABarePrefix(string root, string path, bool inside)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the spellings are POSIX; the Windows comparison is the same code on '\\'");

        RealPath.IsInside(root, path, StringComparison.Ordinal).Should().Be(inside);
    }

    [Fact]
    public void AWindowsRootedTargetWithoutADrive_ResolvesAgainstTheLinksOwnVolume()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a '\\x' target means a drive only on Windows");
        var here = Path.GetPathRoot(_real)!;

        RealPath.Resolve(At("a"), read: p => p == At("a") ? Entry.Link(@"\zz\q") : RealPath.Lstat(p)).Path.Should().Be(Path.Combine(here, "zz", "q"));
    }

    [Fact]
    public void AWindowsDriveRelativeTarget_FailsClosed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "a 'C:x' target means a drive-relative path only on Windows");

        RealPath.Resolve(At("a"), read: p => p == At("a") ? Entry.Link("C:x") : RealPath.Lstat(p)).Ok.Should().BeFalse();
    }
}
