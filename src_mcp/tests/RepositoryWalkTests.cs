using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The walk that finds the Git repositories beneath a granted parent folder (E1.1 of
/// <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>; D2, D3's folder bound, D7): pure over a listing seam, so no test
/// here touches the disk except the one over <see cref="FolderListings.Read"/>.
/// </summary>
public sealed class RepositoryWalkTests
{
    private static readonly string Root = Path.GetFullPath("walk-root");

    private static FolderEntry Dir(string name) => new(name, EntryKind.Folder);

    private static FolderEntry GitDir => new(".git", EntryKind.Folder);

    private static FolderEntry GitFile => new(".git", EntryKind.File);

    private static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>A fake disk: what each path lists as, and the paths the walk asked for — an unknown path lists as empty.</summary>
    private sealed class Disk
    {
        private readonly Dictionary<string, FolderListing> _listings = [];

        public List<string> Asked { get; } = [];

        public Disk Has(string path, params FolderEntry[] entries)
        {
            _listings[path] = new FolderListing.Listed(entries);

            return this;
        }

        public Disk Denies(string path, string reason)
        {
            _listings[path] = new FolderListing.Unreadable(reason);

            return this;
        }

        public FolderListing List(string path)
        {
            Asked.Add(path);

            return _listings.TryGetValue(path, out var listing) ? listing : new FolderListing.Listed([]);
        }
    }

    private static WalkOutcome Walk(Disk disk, CancellationToken ct = default) => RepositoryWalk.Find(Root, disk.List, ct);

    private static WalkOutcome.Found Found(WalkOutcome outcome) =>
        outcome.Should().BeOfType<WalkOutcome.Found>("the walk found what it was asked for, not a refusal").Subject;

    private static string[] Relative(WalkOutcome.Found found) => [.. found.Repositories.Select(one => string.Join('/', one.Segments))];

    [Fact]
    public void ARepositoryAtDepthTwo_IsFound_OneAtDepthThree_IsNot()
    {
        var disk = new Disk()
            .Has(Root, Dir("a"))
            .Has(At("a"), Dir("x"), Dir("y"))
            .Has(At("a", "x"), GitDir)
            .Has(At("a", "y"), Dir("z"))
            .Has(At("a", "y", "z"), GitDir);

        var found = Found(Walk(disk));

        found.Repositories.Should().HaveCount(1);
        found.Repositories[0].Segments.Should().Equal("a", "x");
        found.Repositories[0].Path.Should().Be(At("a", "x"));
        disk.Asked.Should().NotContain(At("a", "y", "z"), "a folder at depth three is never listed");
    }

    [Fact]
    public void AHiddenFolder_NodeModules_AndALink_AreNeverEntered()
    {
        var disk = new Disk()
            .Has(Root, Dir(".hidden"), Dir("node_modules"), Dir("Bin"), Dir("obj"), new FolderEntry("link-repo", EntryKind.Link), Dir("plain"))
            .Has(At(".hidden"), GitDir)
            .Has(At("node_modules"), GitDir)
            .Has(At("Bin"), GitDir)
            .Has(At("obj"), GitDir)
            .Has(At("link-repo"), GitDir)
            .Has(At("plain"), GitDir);

        var found = Found(Walk(disk));

        found.Repositories.Should().HaveCount(1);
        Relative(found).Should().Equal("plain");
        disk.Asked.Should().Equal([Root, At("plain")], "none of the skipped entries is listed, and none of them says a word");
        found.Notes.Should().BeEmpty();
    }

    [Fact]
    public void ACandidateIsNotDescended_SoItsNestedRepositoryAndSubmoduleAreNotFound()
    {
        var disk = new Disk()
            .Has(Root, Dir("repo"))
            .Has(At("repo"), GitDir, Dir("nested"), Dir("lib"))
            .Has(At("repo", "nested"), GitDir)
            .Has(At("repo", "lib"), GitFile);

        var found = Found(Walk(disk));

        found.Repositories.Should().HaveCount(1);
        Relative(found).Should().Equal("repo");
        disk.Asked.Should().NotContain(At("repo", "nested")).And.NotContain(At("repo", "lib"));
    }

    [Fact]
    public void FiftyOneCandidates_AreAllFound_TheBoundIsNotTheWalks()
    {
        var names = Enumerable.Range(0, 51).Select(i => $"repo{i:00}").ToArray();
        var disk = new Disk().Has(Root, [.. names.Select(Dir)]);
        foreach (var name in names)
        {
            disk.Has(At(name), GitDir);
        }

        var found = Found(Walk(disk));

        found.Repositories.Should().HaveCount(51);
    }

    [Fact]
    public void NoRepository_IsRefusedWithTheGitSentence()
    {
        var disk = new Disk().Has(Root, Dir("a"), Dir("b")).Has(At("a"), Dir("c"));

        var refused = Walk(disk).Should().BeOfType<WalkOutcome.Refused>().Subject;

        refused.Reason.Should().Contain("holds no Git repository within two levels").And.Contain(Root)
            .And.Contain("Vibe reads copies coai makes through Git");
    }

    [Fact]
    public void TenThousandAndOneFolders_AreRefused_AndTheWalkStops()
    {
        // 1 root + 101 folders of depth one + 101 x 100 of depth two = 10,202 folders to list.
        var level1 = Enumerable.Range(0, 101).Select(i => $"g{i:000}").ToArray();
        var disk = new Disk().Has(Root, [.. level1.Select(Dir)]);
        foreach (var name in level1)
        {
            disk.Has(At(name), [.. Enumerable.Range(0, 100).Select(i => Dir($"c{i:000}"))]);
        }

        var refused = Walk(disk).Should().BeOfType<WalkOutcome.Refused>().Subject;

        refused.Reason.Should().Contain("more than 10000 folders within two levels").And.Contain("grant the project folders themselves");
        disk.Asked.Count.Should().BeLessThan(RepositoryWalk.MaxFolders + 2, "the walk stops at the bound instead of listing the rest");
    }

    [Fact]
    public void AGitFile_MakesACandidate_AGitLink_DoesNot()
    {
        var disk = new Disk()
            .Has(Root, Dir("worktree"), Dir("linked"))
            .Has(At("worktree"), GitFile)
            .Has(At("linked"), new FolderEntry(".git", EntryKind.Link));

        var found = Found(Walk(disk));

        found.Repositories.Should().HaveCount(1);
        Relative(found).Should().Equal("worktree");
        found.Notes.Should().BeEmpty("a link is skipped silently, like every other link");
    }

    [Fact]
    public void AnUnreadableFolder_IsSkippedWithANote_AndTheOthersAreFound()
    {
        var disk = new Disk()
            .Has(Root, Dir("denied"), Dir("ok"))
            .Denies(At("denied"), "Access is denied")
            .Has(At("ok"), GitDir);

        var found = Found(Walk(disk));

        found.Notes.Should().HaveCount(1);
        found.Notes[0].Should().Be($"{At("denied")} was skipped: it could not be listed (Access is denied)");
        Relative(found).Should().Equal("ok");
    }

    [Fact]
    public void TheOrderIsOrdinalByRelativePath_WhateverTheListingOrder()
    {
        // "a-b" sorts before "a/c" ('-' is below '/'), though the walk reaches "a" first: the result is sorted on the joined path.
        var disk = new Disk()
            .Has(Root, Dir("z"), Dir("a-b"), Dir("a"))
            .Has(At("z"), GitDir)
            .Has(At("a-b"), GitDir)
            .Has(At("a"), Dir("c"))
            .Has(At("a", "c"), GitDir);

        var found = Found(Walk(disk));

        found.Repositories.Should().HaveCount(3);
        Relative(found).Should().Equal("a-b", "a/c", "z");
    }

    [Fact]
    public void ACancelledToken_StopsTheWalkWithOperationCanceledException()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var disk = new Disk().Has(Root, Dir("a")).Has(At("a"), GitDir);

        var walking = () => Walk(disk, cancelled.Token);

        walking.Should().Throw<OperationCanceledException>();
        disk.Asked.Should().BeEmpty("nothing is listed once the budget is gone");
    }

    [Fact]
    public void TheRealListing_TellsAFolderFromAFileFromALink_AndAMissingFolderIsUnreadable()
    {
        using var dir = TempDir.For("testsToDelete-walk-");
        Directory.CreateDirectory(dir.At("a-folder"));
        File.WriteAllText(dir.At("a-file.txt"), "x");
        SnapshotTestKit.LinkNow(dir.At("a-link"), dir.At("a-folder"));

        var listed = FolderListings.Read(dir.Path).Should().BeOfType<FolderListing.Listed>().Subject;

        listed.Entries.Should().HaveCount(3);
        listed.Entries.Should().BeEquivalentTo(
        [
            new FolderEntry("a-folder", EntryKind.Folder),
            new FolderEntry("a-file.txt", EntryKind.File),
            new FolderEntry("a-link", EntryKind.Link),
        ]);
        FolderListings.Read(dir.At("missing")).Should().BeOfType<FolderListing.Unreadable>();
    }
}
