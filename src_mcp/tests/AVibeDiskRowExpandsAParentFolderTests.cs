using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A granted root that is a PARENT of repositories (E1.2 of <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>, D1, D3–D6):
/// <see cref="ConsultantPlaces.OneShotAsync"/> expands it into the repositories beneath it, each copied through the one
/// method a work-tree root goes through. The boundary these tests hold: a candidate git does not confirm is SKIPPED with a
/// note; a copy that is refused REFUSES THE ROW and every copy made is disposed.
/// </summary>
public sealed class AVibeDiskRowExpandsAParentFolderTests : IDisposable
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-vibe-parent-");
    private readonly SnapshotBudget _bytes = SnapshotTestKit.RoomyBudget();

    private string Row => Directory.CreateDirectory(_dir.At("row")).FullName;

    private string Tree => Path.Combine(Row, "tree");

    public void Dispose() => _dir.Dispose();

    private ConsultantPlaces Places() => Places(_ => { });

    private ConsultantPlaces Places(Action<string> note) =>
        new(string.Empty, string.Empty, _launcher, note) { Maker = SnapshotTestKit.Snapshots(_launcher, budget: _bytes) };

    private ConsultantPlaces PlacesWith(SnapshotOptions options) =>
        new(string.Empty, string.Empty, _launcher, _ => { }) { Maker = new ReviewSnapshots(_launcher, options, _bytes, ReviewSnapshots.Quiet) };

    private Task<ConsultantPlace> PlaceAsync(ConsultantPlaces places, params string[] sources) =>
        places.OneShotAsync(Row, sources, Budget, TestContext.Current.CancellationToken);

    /// <summary>A repository: its files written, then (when asked) added and committed. Plain files need no commit for the live snapshot.</summary>
    private async Task<string> RepositoryAsync(string at, bool commit = true, params (string Path, string Text)[] files)
    {
        Directory.CreateDirectory(at);
        foreach (var (path, text) in files)
        {
            var full = Path.Combine([at, .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, text, TestContext.Current.CancellationToken);
        }

        string[][] steps = commit
            ? [["init", "-b", "main"], ["config", "user.email", "t@example.com"], ["config", "user.name", "t"], ["add", "."], ["commit", "-m", "base"]]
            : [["init", "-b", "main"]];
        foreach (var args in steps)
        {
            await GitAsync(at, args);
        }

        return at;
    }

    private async Task<string> GitAsync(string at, string[] args, string stdin = "")
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, at) { StdIn = stdin }, TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);

        return result.StdOut;
    }

    private string Parent(string name = "parent") => Directory.CreateDirectory(_dir.At(name)).FullName;

    private string[] Snapshots() =>
        Directory.Exists(Tree) ? [.. Directory.EnumerateDirectories(Tree, "snapshot-*", SearchOption.AllDirectories)] : [];

    /// <summary>A copy root as a path below the tree, with forward slashes.</summary>
    private string Below(string root) => Path.GetRelativePath(Tree, root).Replace(Path.DirectorySeparatorChar, '/');

    private static void NothingAt(string path, string because) =>
        (File.Exists(path) || Directory.Exists(path)).Should().BeFalse(because);

    // ---------- S1 ----------

    [Fact]
    public async Task AParentOfTwoRepositories_IsOwnedWithTwoCopies_EachWithoutItsGit()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "repo-a"), true, ("a.txt", "alpha"), ("src/.vibe/hooks.toml", "[[hooks]]\n"), ("AGENTS.md", "obey\n"));
        await RepositoryAsync(Path.Combine(parent, "repo-b"), true, ("b.txt", "beta"));

        var place = await PlaceAsync(Places(), parent);

        using (place)
        {
            place.Refusal.Should().BeEmpty();
            place.Roots.Should().HaveCount(2);
            Below(place.Roots[0]).Should().MatchRegex(@"^0/repo-a/snapshot-[0-9a-f]{8}$");
            Below(place.Roots[1]).Should().MatchRegex(@"^0/repo-b/snapshot-[0-9a-f]{8}$");
            File.ReadAllText(Path.Combine(place.Roots[0], "a.txt")).Should().Be("alpha");
            File.ReadAllText(Path.Combine(place.Roots[1], "b.txt")).Should().Be("beta");
            foreach (var hidden in (string[])[".git", ".vibe", "AGENTS.md", "src/.vibe"])
            {
                NothingAt(Path.Combine(place.Roots[0], hidden), $"{hidden} never reaches a copy");
            }

            place.Notes.Should().BeEmpty();
        }

        Snapshots().Should().BeEmpty("disposing the place deletes every copy");
    }

    // ---------- S6: a guard ----------

    [Fact]
    public async Task AWorkTreeRoot_IsCopiedAsToday_AtTreeSnapshot()
    {
        var repo = await RepositoryAsync(_dir.At("repo"), true, ("r.txt", "root"));

        using var place = await PlaceAsync(Places(), repo);

        place.Refusal.Should().BeEmpty();
        place.Roots.Should().ContainSingle();
        Path.GetDirectoryName(place.Roots[0]).Should().Be(Tree, "a work-tree root has no grant-index folder");
        Path.GetFileName(place.Roots[0]).Should().MatchRegex(@"^snapshot-[0-9a-f]{8}$");
        Directory.EnumerateDirectories(Tree).Should().ContainSingle();
    }

    [Fact]
    public async Task AGrantedFolderThatDoesNotExist_StaysARefusal()
    {
        using var place = await PlaceAsync(Places(), _dir.At("no-such-folder"));

        place.Refusal.Should().Contain("does not exist");
    }

    // ---------- S7: the budget ----------

    [Fact]
    public async Task TheBudgetRunsOutInTheSecondOfThreeCopies_RefusesNamingOneOfThree_AndLeavesNoSnapshot()
    {
        var parent = Parent();
        foreach (var name in (string[])["a-fast", "b-slow", "c-fast"])
        {
            await RepositoryAsync(Path.Combine(parent, name), true, (name + ".txt", name));
        }

        var places = PlacesWith(SnapshotOptions.Default with
        {
            // The slow repository's copy outlasts the whole budget. The budget is wide enough for the three git confirmations
            // of a busy runner to finish inside it (a 4 s budget was tight there), and the sleep is wider still.
            BeforeCopyOf = (_, path) =>
            {
                if (path.Contains("b-slow", StringComparison.OrdinalIgnoreCase))
                {
                    Thread.Sleep(TimeSpan.FromSeconds(12));
                }
            },
        });

        using var place = await places.OneShotAsync(Row, [parent], TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        place.Refusal.Should().Contain("1 of 3", "three repositories were confirmed, one was copied").And.Contain("budget");
        Snapshots().Should().BeEmpty("the copy that was made is disposed");
        _bytes.Held.Should().Be(0, "and its bytes are given back");
    }

    [Fact]
    public async Task TheBudgetRunsOutDuringTheWalk_RefusesWithNothingMade()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "repo-a"), true, ("a.txt", "alpha"));
        var places = Places();
        var first = true;
        places.Listing = path =>
        {
            if (first)
            {
                first = false;
                Thread.Sleep(TimeSpan.FromSeconds(2));
            }

            return FolderListings.Read(path);
        };

        using var place = await places.OneShotAsync(Row, [parent], TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        place.Refusal.Should().Contain("budget").And.Contain("0 of 0");
        Snapshots().Should().BeEmpty();
        _bytes.Held.Should().Be(0);
    }

    [Fact]
    public async Task TheCallersOwnCancellation_LeavesAsACancellation_AndDisposesEveryCopy()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "a-first"), true, ("a-first.txt", "alpha"));
        await RepositoryAsync(Path.Combine(parent, "b-second"), true, ("b-second.txt", "beta"));
        using var withdrawn = new CancellationTokenSource();
        var places = PlacesWith(SnapshotOptions.Default with
        {
            BeforeCopyOf = (_, path) =>
            {
                if (path.Contains("b-second", StringComparison.OrdinalIgnoreCase))
                {
                    withdrawn.Cancel();
                }
            },
        });

        var placing = () => places.OneShotAsync(Row, [parent], Budget, withdrawn.Token);

        await placing.Should().ThrowAsync<OperationCanceledException>();
        Snapshots().Should().BeEmpty("the first copy is disposed on the way out");
        _bytes.Held.Should().Be(0);
    }

    // ---------- S8: the snapshot's exclusions, per copy ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ACommittedVibeHooksFile_IsNotInTheCopy(bool underAParent)
    {
        var parent = Parent();
        var repo = await RepositoryAsync(Path.Combine(parent, "repo"), true,
            ("src/app.txt", "app"), (".vibe/hooks.toml", "[[hooks]]\n"), (".agents/skills/x/SKILL.md", "skill"), (".mcp.json", "{}"));

        using var place = await PlaceAsync(Places(), underAParent ? parent : repo);

        place.Refusal.Should().BeEmpty();
        var root = place.Roots.Should().ContainSingle().Subject;
        File.Exists(Path.Combine(root, "src", "app.txt")).Should().BeTrue();
        foreach (var excluded in (string[])[".vibe", ".agents", ".mcp.json", ".git"])
        {
            NothingAt(Path.Combine(root, excluded), $"{excluded} is stripped from every copy");
        }
    }

    // ---------- S11: a candidate git does not confirm is skipped; an unreadable folder too ----------

    [Fact]
    public async Task ABrokenGitFile_AndAnUnreadableFolder_AreSkippedWithANoteEach_AndTheOthersCopied()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "good"), true, ("g.txt", "good"));
        VibeQuestionRowFixture.BrokenCheckout(Path.Combine(parent, "broken"));
        var denied = Directory.CreateDirectory(Path.Combine(parent, "denied")).FullName;
        List<string> said = [];
        var places = Places(said.Add);
        places.Listing = path => path == denied ? new FolderListing.Unreadable("Access is denied") : FolderListings.Read(path);

        using var place = await PlaceAsync(places, parent);

        place.Notes.Should().HaveCount(2);
        place.Notes[0].Should().Contain(denied).And.Contain("could not be listed").And.Contain("Access is denied");
        place.Notes[1].Should().Contain(Path.Combine(parent, "broken")).And.Contain("git does not confirm it as a work tree");
        said.Should().Equal(place.Notes, "the sink the log reads gets every note too");
        place.Refusal.Should().BeEmpty();
        Below(place.Roots.Should().ContainSingle().Subject).Should().StartWith("0/good/snapshot-");
    }

    [Fact]
    public async Task AParentWithOnlyABrokenRepository_IsRefusedWithTheNoneSentence_AndKeepsItsNote()
    {
        var parent = Parent();
        VibeQuestionRowFixture.BrokenCheckout(Path.Combine(parent, "broken"));

        using var place = await PlaceAsync(Places(), parent);

        place.Refusal.Should().Contain("holds no Git repository");
        place.Notes.Should().ContainSingle().Which.Should().Contain("git does not confirm it as a work tree");
    }

    /// <summary>
    /// The bound counts CONFIRMED repositories (D3, the cadence consult): fifty good ones and a broken <c>.git</c> file are Owned
    /// with fifty copies; a fifty-first good one refuses the row — the same sentence on every run, before any copy is made.
    /// One test, because each repository costs a git process: the fifty-first is added to the fifty already there.
    /// </summary>
    [Fact]
    public async Task TheBoundCountsConfirmedRepositories_FiftyAndABrokenOneAreOwned_FiftyOneAreRefusedBeforeAnyCopy()
    {
        var parent = Parent();
        await InitManyAsync(parent, 50);
        VibeQuestionRowFixture.BrokenCheckout(Path.Combine(parent, "zz-broken"));
        var copies = 0;
        var places = PlacesWith(SnapshotOptions.Default with { BeforeCopyOf = (_, _) => Interlocked.Increment(ref copies) });
        var roomy = TimeSpan.FromMinutes(10);

        using (var owned = await places.OneShotAsync(Row, [parent], roomy, TestContext.Current.CancellationToken))
        {
            owned.Refusal.Should().BeEmpty("a broken candidate never counts toward the bound");
            owned.Roots.Should().HaveCount(50);
            owned.Notes.Should().ContainSingle();
        }

        await RepositoryAsync(Path.Combine(parent, "zz-extra"), false, ("extra.txt", "one too many"));
        copies = 0;
        using var first = await places.OneShotAsync(Row, [parent], roomy, TestContext.Current.CancellationToken);
        using var second = await places.OneShotAsync(Row, [parent], roomy, TestContext.Current.CancellationToken);

        first.Refusal.Should().Contain("more than 50 Git repositories").And.Contain("grant the project folders themselves");
        second.Refusal.Should().Be(first.Refusal, "the same root gives the same sentence on every run");
        copies.Should().Be(0, "every candidate is validated before the first is copied");
        Snapshots().Should().BeEmpty();
    }

    private async Task InitManyAsync(string parent, int count)
    {
        await Task.WhenAll(Enumerable.Range(0, count).Select(i =>
            RepositoryAsync(Path.Combine(parent, $"repo{i:00}"), false, ($"f{i}.txt", i.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
    }

    // ---------- S12: a copy that is refused refuses the row ----------

    [Fact]
    public async Task TheSecondRepositorysCopyIsRefused_RefusesTheRow_AndDisposesTheFirst()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "a-clean"), true, ("a.txt", "alpha"));
        var second = await RepositoryAsync(Path.Combine(parent, "b-linked"), true, ("b.txt", "beta"));
        // A symbolic link git LISTS: a committed entry of mode 120000, written into the index — no link on disk, no privilege.
        var blob = await GitAsync(second, ["hash-object", "-w", "--stdin"], _dir.At("elsewhere"));
        await GitAsync(second, ["update-index", "--add", "--cacheinfo", $"120000,{blob.Trim()},escape"]);
        await GitAsync(second, ["commit", "-m", "a link out"]);

        using var place = await PlaceAsync(Places(), parent);

        place.Refusal.Should().Contain("escape");
        place.Roots.Should().BeEmpty();
        Snapshots().Should().BeEmpty("the first copy is disposed with the refusal");
        _bytes.Held.Should().Be(0);
    }

    // ---------- S13: the link boundary reaches up to the grant ----------

    [Fact]
    public async Task AGroupingFolderReplacedByALinkBetweenTheWalkAndTheCopy_RefusesTheRow()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "group", "repo"), true, ("r.txt", "root"));
        var group = Path.Combine(parent, "group");
        var places = Places();
        places.BeforeCopyOf = _ =>
        {
            Directory.Move(group, group + "-aside");
            SnapshotTestKit.LinkNow(group, group + "-aside");
        };

        using var place = await PlaceAsync(places, parent);

        place.Refusal.Should().Contain("is a link, and the granted folder");
        Snapshots().Should().BeEmpty();
    }

    [Fact]
    public async Task AGrantedRootThatIsALink_IsRefusedBeforeTheWalk()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "repo"), true, ("r.txt", "root"));
        var link = _dir.At("granted-link");
        await DirectoryLink.MakeAsync(_launcher, link, parent);
        var places = Places();
        var walked = false;
        places.Listing = path =>
        {
            walked = true;

            return FolderListings.Read(path);
        };

        using var place = await PlaceAsync(places, link);

        place.Refusal.Should().Contain("is a link, and the granted folder");
        walked.Should().BeFalse("a root that is a link is never walked through");
        Snapshots().Should().BeEmpty();
    }

    // ---------- S14: de-duplication and the grant index ----------

    [Fact]
    public async Task TwoGrantsEachHoldingGroupRepo_GiveTwoCopies_UnderDifferentGrantIndexes()
    {
        var one = Parent("one");
        var two = Parent("two");
        await RepositoryAsync(Path.Combine(one, "group", "repo"), true, ("one.txt", "one"));
        await RepositoryAsync(Path.Combine(two, "group", "repo"), true, ("two.txt", "two"));

        using var place = await PlaceAsync(Places(), one, two);

        place.Refusal.Should().BeEmpty();
        place.Roots.Should().HaveCount(2);
        Below(place.Roots[0]).Should().StartWith("0/group/repo/snapshot-");
        Below(place.Roots[1]).Should().StartWith("1/group/repo/snapshot-");
        File.Exists(Path.Combine(place.Roots[0], "one.txt")).Should().BeTrue();
        File.Exists(Path.Combine(place.Roots[1], "two.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task TwoOverlappingGrants_CopyTheSharedRepositoryOnce()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "group", "repo"), true, ("r.txt", "root"));

        using var place = await PlaceAsync(Places(), parent, Path.Combine(parent, "group"));

        place.Refusal.Should().BeEmpty("a repository found twice is not a failure");
        place.Roots.Should().ContainSingle();
    }

    [Fact]
    public async Task ARepositoryGrantedDirectlyAndFoundUnderAParent_IsCopiedOnce_TheFirstGrantWins()
    {
        var parent = Parent();
        var shared = await RepositoryAsync(Path.Combine(parent, "shared"), true, ("s.txt", "shared"));
        await RepositoryAsync(Path.Combine(parent, "other"), true, ("o.txt", "other"));

        using var place = await PlaceAsync(Places(), shared, parent);

        place.Refusal.Should().BeEmpty();
        place.Roots.Should().HaveCount(2, "shared once (as the direct grant), other once");
        Path.GetDirectoryName(place.Roots[0]).Should().Be(Tree, "the first in source order wins: the direct grant, at the tree's top");
        Below(place.Roots[1]).Should().StartWith("1/other/snapshot-");
    }

    // ---------- D9: a linked worktree whose main checkout is copied is skipped (S16-S19) ----------

    private const string OneSkippedSentence =
        "skipped 1 linked worktree whose main checkout is copied — its branch and uncommitted files were not read; grant a worktree directly to include it";

    private async Task<string> WorktreeAsync(string main, string at, string branch)
    {
        await GitAsync(main, ["worktree", "add", "-b", branch, at]);

        return at;
    }

    [Fact]
    public async Task AMainCheckoutAndThreeLinkedWorktreesUnderTheGrant_GiveOneCopy_AndOneSummaryNote()
    {
        var parent = Parent();
        var main = await RepositoryAsync(Path.Combine(parent, "app"), true, ("app.txt", "main"));
        foreach (var n in (int[])[1, 2, 3])
        {
            await WorktreeAsync(main, Path.Combine(parent, $"app-wt{n}"), $"wt{n}");
        }

        List<string> said = [];

        using var place = await PlaceAsync(Places(said.Add), parent);

        place.Refusal.Should().BeEmpty();
        Below(place.Roots.Should().ContainSingle("the three worktrees are the main checkout's own branches").Subject).Should().StartWith("0/app/snapshot-");
        place.Notes.Should().ContainSingle().Which.Should().Be(
            "skipped 3 linked worktrees whose main checkouts are copied — their branches and uncommitted files were not read; grant a worktree directly to include it");
        said.Should().Equal(place.Notes, "the log's sink gets the summary too");
    }

    [Fact]
    public async Task ASingleLinkedWorktree_IsSaidInTheSingular()
    {
        var parent = Parent();
        var main = await RepositoryAsync(Path.Combine(parent, "app"), true, ("app.txt", "main"));
        await WorktreeAsync(main, Path.Combine(parent, "app-wt"), "wt");

        using var place = await PlaceAsync(Places(), parent);

        place.Notes.Should().ContainSingle().Which.Should().Be(OneSkippedSentence);
    }

    [Fact]
    public async Task ALinkedWorktreeWhoseMainCheckoutIsOutsideTheGrant_IsCopied()
    {
        var main = await RepositoryAsync(_dir.At("elsewhere", "app"), true, ("app.txt", "main"));
        var parent = Parent();
        await WorktreeAsync(main, Path.Combine(parent, "app-wt"), "wt");

        using var place = await PlaceAsync(Places(), parent);

        place.Refusal.Should().BeEmpty();
        Below(place.Roots.Should().ContainSingle().Subject).Should().StartWith("0/app-wt/snapshot-");
        File.Exists(Path.Combine(place.Roots[0], "app.txt")).Should().BeTrue();
        place.Notes.Should().BeEmpty("nothing was skipped: no main checkout of it is copied");
    }

    [Fact]
    public async Task ALinkedWorktreeWhoseMainCheckoutCouldNotBeConfirmed_IsKept_BecauseItsIdentityIsUncertain()
    {
        var parent = Parent();
        var main = await RepositoryAsync(Path.Combine(parent, "app"), true, ("app.txt", "main"));
        await WorktreeAsync(main, Path.Combine(parent, "app-wt"), "wt");
        // The main's own HEAD is garbage: git refuses the main (not a repository), while the worktree keeps its own HEAD.
        await File.WriteAllTextAsync(Path.Combine(main, ".git", "HEAD"), "garbage\n", TestContext.Current.CancellationToken);

        using var place = await PlaceAsync(Places(), parent);

        place.Refusal.Should().BeEmpty();
        Below(place.Roots.Should().ContainSingle("only the worktree was confirmed, so it is the one copy").Subject).Should().StartWith("0/app-wt/snapshot-");
        place.Notes.Should().ContainSingle().Which.Should().Contain("app").And.Contain("git does not confirm it as a work tree");
    }

    [Fact]
    public async Task AWorktreeGrantedDirectly_IsAlwaysCopied_EvenWhenItsMainCheckoutIsCopiedToo()
    {
        var parent = Parent();
        var main = await RepositoryAsync(Path.Combine(parent, "app"), true, ("app.txt", "main"));
        var direct = await WorktreeAsync(main, _dir.At("direct-wt"), "wt");

        using var place = await PlaceAsync(Places(), parent, direct);

        place.Refusal.Should().BeEmpty();
        place.Roots.Should().HaveCount(2, "the main checkout through the parent, the worktree because it was granted by name");
        place.Roots.Select(Below).Should().Contain(root => root.StartsWith("0/app/snapshot-")).And.Contain(root => root.StartsWith("snapshot-"));
        place.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task AWorktreeGrantedDirectlyAndFoundUnderTheParent_IsCopiedOnce_AndIsNotCountedAsSkipped()
    {
        var parent = Parent();
        var main = await RepositoryAsync(Path.Combine(parent, "app"), true, ("app.txt", "main"));
        var inside = await WorktreeAsync(main, Path.Combine(parent, "app-wt"), "wt");

        using var place = await PlaceAsync(Places(), inside, parent);

        place.Refusal.Should().BeEmpty();
        place.Roots.Should().HaveCount(2, "the worktree once (granted first), the main checkout once");
        place.Notes.Should().BeEmpty("the worktree was copied, so it is not one of the skipped");
    }

    [Fact]
    public async Task ACheckoutWhoseGitIsAFile_ButIsNotLinked_IsNeverTakenForALinkedWorktree()
    {
        // A `.git` FILE says nothing about being linked: a submodule's top has one, so does `git init --separate-git-dir`.
        // What decides is git's answer — the absolute git dir against the common dir — never the file's text.
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "app"), true, ("app.txt", "main"));
        var lib = Directory.CreateDirectory(Path.Combine(parent, "lib")).FullName;
        await File.WriteAllTextAsync(Path.Combine(lib, "lib.txt"), "lib", TestContext.Current.CancellationToken);
        await GitAsync(lib, ["init", "-b", "main", "--separate-git-dir", _dir.At("lib-gitdir")]);

        using var place = await PlaceAsync(Places(), parent);

        File.Exists(Path.Combine(lib, ".git")).Should().BeTrue("the checkout's .git is a file");
        place.Refusal.Should().BeEmpty();
        place.Roots.Should().HaveCount(2);
        place.Notes.Should().BeEmpty();
    }

    // ---------- gap (a): a found candidate must be the TOP of a work tree ----------

    [Fact]
    public async Task AFolderWithAnEmptyGit_InsideAnotherRepository_IsNotATop_SkippedWithANote_NeverCopied()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "good"), true, ("g.txt", "good"));
        var other = await RepositoryAsync(_dir.At("other"), true, ("o.txt", "other"));
        // X is not a candidate: its .git is a link (to ANOTHER repository's git dir), so the walk goes into it.
        var x = Directory.CreateDirectory(Path.Combine(parent, "X")).FullName;
        await DirectoryLink.MakeAsync(_launcher, Path.Combine(x, ".git"), Path.Combine(other, ".git"));
        // X/sub holds an EMPTY .git folder: a candidate to the walk, but git, finding no repository there, climbs to X's.
        var sub = Directory.CreateDirectory(Path.Combine(x, "sub")).FullName;
        Directory.CreateDirectory(Path.Combine(sub, ".git"));
        await File.WriteAllTextAsync(Path.Combine(sub, "s.txt"), "inside X", TestContext.Current.CancellationToken);

        using var place = await PlaceAsync(Places(), parent);

        place.Refusal.Should().BeEmpty();
        Below(place.Roots.Should().ContainSingle("a folder inside another work tree is no repository top").Subject).Should().StartWith("0/good/snapshot-");
        place.Notes.Should().ContainSingle().Which.Should().Contain(sub).And.Contain("git does not confirm it as a work tree");
    }

    // ---------- gap (b): a root that could not be listed says so, and keeps what the walk skipped ----------

    [Fact]
    public async Task ARootThatCouldNotBeListed_IsRefusedWithItsOwnSentence()
    {
        var parent = Parent();
        var places = Places();
        places.Listing = path => path == parent ? new FolderListing.Unreadable("Access is denied") : FolderListings.Read(path);

        using var place = await PlaceAsync(places, parent);

        place.Refusal.Should().Contain(parent).And.Contain("could not be listed (Access is denied)").And.Contain("grant a folder coai can read");
        place.Refusal.Should().NotContain("holds no Git repository", "an unreadable folder is not an empty one");
    }

    [Fact]
    public async Task ARootWhoseEveryChildCouldNotBeListed_IsRefusedAndKeepsOneNoteEach()
    {
        var parent = Parent();
        var one = Directory.CreateDirectory(Path.Combine(parent, "one")).FullName;
        var two = Directory.CreateDirectory(Path.Combine(parent, "two")).FullName;
        var places = Places();
        places.Listing = path => path == one || path == two ? new FolderListing.Unreadable("Access is denied") : FolderListings.Read(path);

        using var place = await PlaceAsync(places, parent);

        place.Refusal.Should().Contain("holds no Git repository");
        place.Notes.Should().HaveCount(2).And.OnlyContain(note => note.Contains("could not be listed"));
    }

    [Fact]
    public async Task ARootWithTooManyFolders_IsRefusedAndKeepsTheNotesTheWalkHadMade()
    {
        var parent = Parent();
        var first = Path.Combine(parent, "f00000");
        var places = Places();
        places.Listing = path =>
            path == parent ? new FolderListing.Listed([.. Enumerable.Range(0, RepositoryWalk.MaxFolders + 1).Select(i => new FolderEntry($"f{i:00000}", CoaiMcp.Server.EntryKind.Folder))])
            : path == first ? new FolderListing.Unreadable("Access is denied")
            : new FolderListing.Listed([]);

        using var place = await PlaceAsync(places, parent);

        place.Refusal.Should().Contain("more than 10000 folders");
        place.Notes.Should().ContainSingle().Which.Should().Contain(first).And.Contain("could not be listed");
    }

    // ---------- the bound is the ROW's ----------

    [Fact]
    public async Task TheBoundSentenceNamesTheGrantedFolders_NotTheLastRoot()
    {
        var one = Parent("one");
        var two = Parent("two");
        await InitManyAsync(one, 26);
        await InitManyAsync(two, 25);

        using var place = await PlaceAsync(Places(), one, two);

        place.Refusal.Should().Contain("the granted folders hold more than 50 Git repositories").And.Contain("grant the project folders themselves");
        place.Refusal.Should().NotContain(two, "the second root alone holds 25; the ROW holds 51");
        Snapshots().Should().BeEmpty("the bound is judged before any copy");
    }

    [Fact]
    public async Task AGrantedWorkTree_CountsTowardTheSameBound_WhereverItStandsInTheList()
    {
        var parent = Parent();
        await InitManyAsync(parent, 50);
        var direct = await RepositoryAsync(_dir.At("direct"), false, ("d.txt", "the fifty-first"));
        var copies = 0;
        var places = PlacesWith(SnapshotOptions.Default with { BeforeCopyOf = (_, _) => Interlocked.Increment(ref copies) });

        using var place = await places.OneShotAsync(Row, [parent, direct], TimeSpan.FromMinutes(10), TestContext.Current.CancellationToken);

        place.Refusal.Should().Contain("the granted folders hold more than 50 Git repositories");
        copies.Should().Be(0, "the directly granted work tree is counted before the first copy, so the row is refused before any");
    }

    // ---------- the walk's cost ----------

    [Fact]
    public async Task ADepthTwoFolder_IsNeverListed_OnlyItsGitIsLookedFor()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "group", "repo"), true, ("r.txt", "root"));
        Directory.CreateDirectory(Path.Combine(parent, "group", "plain"));
        List<string> listed = [];
        var places = Places();
        places.Listing = path =>
        {
            listed.Add(path);

            return FolderListings.Read(path);
        };

        using var place = await PlaceAsync(places, parent);

        place.Refusal.Should().BeEmpty();
        place.Roots.Should().ContainSingle();
        listed.Should().BeEquivalentTo([parent, Path.Combine(parent, "group")], "the root and its children are listed; their children are only asked about .git");
    }

    [Fact]
    public async Task AFolderLinkSkippedByTheWalk_IsNamedInANote_NotSkippedInSilence()
    {
        var parent = Parent();
        await RepositoryAsync(Path.Combine(parent, "good"), true, ("g.txt", "good"));
        var elsewhere = Directory.CreateDirectory(_dir.At("cloud")).FullName;
        await DirectoryLink.MakeAsync(_launcher, Path.Combine(parent, "OneDrive"), elsewhere);

        using var place = await PlaceAsync(Places(), parent);

        place.Refusal.Should().BeEmpty();
        place.Roots.Should().ContainSingle();
        place.Notes.Should().ContainSingle().Which.Should().Contain("OneDrive").And.Contain("link");
    }
}
