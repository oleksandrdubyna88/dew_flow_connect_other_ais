using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The walk that finds the Git repositories beneath a granted parent folder (E1.1 of
/// <c>research/PLAN_vibe_reads_a_folder_of_repositories.md</c>; D2, D3's folder bound, D7): pure over a listing seam, so no test
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

        public Disk TooMany(string path)
        {
            _listings[path] = new FolderListing.TooMany();

            return this;
        }

        public Disk Denies(string path, string reason)
        {
            _listings[path] = new FolderListing.Unreadable(reason);

            return this;
        }

        /// <summary>The paths the walk only looked for a <c>.git</c> in — the folders of the deepest level, never listed.</summary>
        public List<string> Probed { get; } = [];

        public FolderListing List(string path)
        {
            Asked.Add(path);

            return Listing(path);
        }

        /// <summary>What <see cref="FolderListings.GitEntryOf"/> answers: the folder's <c>.git</c> entry, none, or why not.</summary>
        public FolderListing Probe(string path)
        {
            Probed.Add(path);

            return Listing(path) is FolderListing.Listed listed
                ? new FolderListing.Listed([.. listed.Entries.Where(entry => entry.Name == ".git")])
                : Listing(path);
        }

        private FolderListing Listing(string path) =>
            _listings.TryGetValue(path, out var listing) ? listing : new FolderListing.Listed([]);
    }

    private static WalkOutcome Walk(Disk disk, CancellationToken ct = default) => RepositoryWalk.Find(Root, disk.List, disk.Probe, ct);

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
        (disk.Asked.Count + disk.Probed.Count).Should().BeLessThan(RepositoryWalk.MaxFolders + 2, "the walk stops at the bound instead of listing the rest");
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
    public void TheRealListing_TellsAFolderFromALink_DropsAPlainFile_AndAMissingFolderIsUnreadable()
    {
        using var dir = TempDir.For("testsToDelete-walk-");
        Directory.CreateDirectory(dir.At("a-folder"));
        File.WriteAllText(dir.At("a-file.txt"), "x");
        SnapshotTestKit.LinkNow(dir.At("a-link"), dir.At("a-folder"));

        var listed = FolderListings.Read(dir.Path).Should().BeOfType<FolderListing.Listed>().Subject;

        listed.Entries.Should().HaveCount(2, "a plain file is read and let go: the walk needs folders, links to folders and a .git file only");
        listed.Entries.Should().BeEquivalentTo(
        [
            new FolderEntry("a-folder", EntryKind.Folder),
            new FolderEntry("a-link", EntryKind.FolderLink),
        ]);
        FolderListings.Read(dir.At("missing")).Should().BeOfType<FolderListing.Unreadable>();
    }

    [Fact]
    public void TheRealGitLookup_SeesAFolderAFileAndALink_ForOneNameOnly_AndNoneWhereThereIsNone()
    {
        using var dir = TempDir.For("testsToDelete-walk-");
        Directory.CreateDirectory(dir.At("with-folder", ".git"));
        Directory.CreateDirectory(dir.At("with-file"));
        File.WriteAllText(dir.At("with-file", ".git"), "gitdir: /elsewhere");
        Directory.CreateDirectory(dir.At("with-link"));
        Directory.CreateDirectory(dir.At("target"));
        SnapshotTestKit.LinkNow(dir.At("with-link", ".git"), dir.At("target"));
        Directory.CreateDirectory(dir.At("none"));
        File.WriteAllText(dir.At("none", "other.txt"), "x");

        Entries(FolderListings.GitEntryOf(dir.At("with-folder"))).Should().Equal(new FolderEntry(".git", EntryKind.Folder));
        Entries(FolderListings.GitEntryOf(dir.At("with-file"))).Should().Equal(new FolderEntry(".git", EntryKind.File));
        Entries(FolderListings.GitEntryOf(dir.At("with-link"))).Should().Equal(new FolderEntry(".git", EntryKind.FolderLink));
        Entries(FolderListings.GitEntryOf(dir.At("none"))).Should().BeEmpty("a folder without a .git answers none, and its other files are never read");
        Entries(FolderListings.GitEntryOf(dir.At("vanished"))).Should().BeEmpty("a folder that is gone holds no .git");
    }

    private static IReadOnlyList<FolderEntry> Entries(FolderListing listing) =>
        listing.Should().BeOfType<FolderListing.Listed>().Subject.Entries;

    // ---------- the walk's second seam: the deepest level is only asked for its .git ----------

    [Fact]
    public void ADepthTwoFolder_IsOnlyProbedForItsGit_NeverListed()
    {
        var disk = new Disk()
            .Has(Root, Dir("a"))
            .Has(At("a"), Dir("x"), Dir("y"))
            .Has(At("a", "x"), GitDir)
            .Has(At("a", "y"), Dir("big"));

        var found = Found(Walk(disk));

        Relative(found).Should().Equal("a/x");
        disk.Asked.Should().Equal([Root, At("a")], "the root and its children are listed");
        disk.Probed.Should().Equal([At("a", "x"), At("a", "y")], "their children are only asked about a .git, in the walk's order");
    }

    [Fact]
    public void ADepthTwoFolderThatCouldNotBeProbed_IsSkippedWithTheSameNote()
    {
        var disk = new Disk()
            .Has(Root, Dir("a"))
            .Has(At("a"), Dir("denied"), Dir("ok"))
            .Denies(At("a", "denied"), "Access is denied")
            .Has(At("a", "ok"), GitDir);

        var found = Found(Walk(disk));

        Relative(found).Should().Equal("a/ok");
        found.Notes.Should().ContainSingle().Which.Should().Be($"{At("a", "denied")} was skipped: it could not be listed (Access is denied)");
    }

    // ---------- a root that cannot be listed is not an empty one ----------

    [Fact]
    public void ARootThatCannotBeListed_IsRefusedByName_NotReportedAsEmpty()
    {
        var disk = new Disk().Denies(Root, "Access is denied");

        var refused = Walk(disk).Should().BeOfType<WalkOutcome.Refused>().Subject;

        refused.Reason.Should().Be($"the folder {Root} could not be listed (Access is denied) — grant a folder coai can read");
        refused.Notes.Should().BeEmpty();
    }

    [Fact]
    public void ARefusalCarriesWhatTheWalkSkippedBeforeIt()
    {
        var disk = new Disk().Has(Root, Dir("denied"), Dir("plain")).Denies(At("denied"), "Access is denied");

        var refused = Walk(disk).Should().BeOfType<WalkOutcome.Refused>().Subject;

        refused.Reason.Should().Contain("holds no Git repository");
        refused.Notes.Should().ContainSingle().Which.Should().Contain(At("denied")).And.Contain("could not be listed");
    }

    // ---------- a link to a folder is named, not skipped in silence ----------

    [Fact]
    public void LinksToFolders_AreNamedInOneNote_AndLinksToFilesAndHiddenOrNoiseLinksAreNot()
    {
        var disk = new Disk()
            .Has(Root, Dir("repo"), new FolderEntry("OneDrive", EntryKind.FolderLink), new FolderEntry("Dropbox", EntryKind.FolderLink),
                new FolderEntry("a-file-link", EntryKind.Link), new FolderEntry(".cache", EntryKind.FolderLink), new FolderEntry("node_modules", EntryKind.FolderLink))
            .Has(At("repo"), GitDir);

        var found = Found(Walk(disk));

        found.Notes.Should().ContainSingle().Which.Should().Be(
            $"{Root}: 2 linked folders were not entered, because a copy never follows a link (Dropbox, OneDrive)");
    }

    [Fact]
    public void ALinkedFolderNote_NamesFiveAndCountsTheRest_AndIsSingular_ForOne()
    {
        var many = Enumerable.Range(1, 7).Select(i => new FolderEntry($"link{i}", EntryKind.FolderLink)).ToArray();
        var disk = new Disk().Has(Root, [Dir("repo"), .. many]).Has(At("repo"), GitDir);

        Found(Walk(disk)).Notes.Should().ContainSingle().Which.Should().Be(
            $"{Root}: 7 linked folders were not entered, because a copy never follows a link (link1, link2, link3, link4, link5 and 2 more)");

        var one = new Disk().Has(Root, Dir("repo"), new FolderEntry("cloud", EntryKind.FolderLink)).Has(At("repo"), GitDir);

        Found(Walk(one)).Notes.Should().ContainSingle().Which.Should().Be(
            $"{Root}: 1 linked folder was not entered, because a copy never follows a link (cloud)");
    }

    // ---------- finding 4: a folder the system hides is skipped like a dot folder ----------

    [Fact]
    public void AFolderTheSystemHides_IsNeverEntered_AndSaysNothing()
    {
        var disk = new Disk()
            .Has(Root, new FolderEntry("$RECYCLE.BIN", EntryKind.Folder, Hidden: true), new FolderEntry("System Volume Information", EntryKind.Folder, Hidden: true),
                new FolderEntry("hidden-link", EntryKind.FolderLink, Hidden: true), Dir("plain"))
            .Has(At("$RECYCLE.BIN"), GitDir)
            .Has(At("System Volume Information"), GitDir)
            .Has(At("plain"), GitDir);

        var found = Found(Walk(disk));

        Relative(found).Should().Equal("plain");
        disk.Asked.Should().Equal([Root, At("plain")], "a hidden or system folder is not listed");
        found.Notes.Should().BeEmpty("it is skipped without a word, like a dot folder — a hidden link too");
    }

    [Fact]
    public void TheRealListing_CarriesTheHiddenAndSystemAttributes_OnWindows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the Hidden and System attributes of a folder are Windows'");
        using var dir = TempDir.For("testsToDelete-walk-hidden-");
        Directory.CreateDirectory(dir.At("plain"));
        Directory.CreateDirectory(dir.At("hidden"));
        Directory.CreateDirectory(dir.At("system"));
        File.SetAttributes(dir.At("hidden"), FileAttributes.Directory | FileAttributes.Hidden);
        File.SetAttributes(dir.At("system"), FileAttributes.Directory | FileAttributes.System);

        var entries = Entries(FolderListings.Read(dir.Path));

        entries.Single(entry => entry.Name == "hidden").Hidden.Should().BeTrue();
        entries.Single(entry => entry.Name == "system").Hidden.Should().BeTrue();
        entries.Single(entry => entry.Name == "plain").Hidden.Should().BeFalse();
    }

    // ---------- finding 6: a listing is bounded ----------

    [Fact]
    public void AFolderWithTooManyEntries_RefusesTheRow_KeepsTheNotes_AndTheWalkStops()
    {
        var disk = new Disk()
            .Has(Root, Dir("denied"), Dir("huge"), Dir("never"))
            .Denies(At("denied"), "Access is denied")
            .TooMany(At("huge"))
            .Has(At("never"), GitDir);

        var refused = Walk(disk).Should().BeOfType<WalkOutcome.Refused>().Subject;

        refused.Reason.Should().Be($"the folder {At("huge")} holds more than {RepositoryWalk.MaxEntries} entries — grant the project folders themselves");
        refused.Notes.Should().ContainSingle().Which.Should().Contain(At("denied"));
        disk.Asked.Should().NotContain(At("never"), "the walk stops at the folder that is too big");
    }

    [Fact]
    public void ARootWithTooManyEntries_IsRefusedByName()
    {
        var disk = new Disk().TooMany(Root);

        Walk(disk).Should().BeOfType<WalkOutcome.Refused>().Which.Reason.Should().Contain(Root).And.Contain("holds more than 100000 entries");
    }

    [Fact]
    public void TheRealListing_StopsAtTheBound_AndKeepsOnlyWhatTheWalkNeeds()
    {
        using var dir = TempDir.For("testsToDelete-walk-bound-");
        foreach (var i in Enumerable.Range(0, 5))
        {
            File.WriteAllText(dir.At($"file{i}.txt"), "x");
        }

        FolderListings.Read(dir.Path, 4).Should().BeOfType<FolderListing.TooMany>("five entries are past a bound of four");

        Directory.CreateDirectory(dir.At("a-folder"));
        File.WriteAllText(dir.At(".git"), "gitdir: /elsewhere");
        var listed = FolderListings.Read(dir.Path, 100).Should().BeOfType<FolderListing.Listed>().Subject;

        listed.Entries.Select(entry => entry.Name).Should().BeEquivalentTo(["a-folder", ".git"], "plain files are counted while reading and never kept");
    }
}
