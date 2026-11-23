using System.Collections;
using System.Text.Json;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s4, spec E.2 and E.1 — the BA's files are published ATOMICALLY: written into <c>&lt;epic&gt;/.coai-ba-staging-&lt;jobId&gt;/</c>, the manifest last, and moved into <c>stories/</c> with ONE directory rename. A publisher stopped at
/// every write boundary — as a killed process would be — leaves either no <c>stories/</c> change or the complete set; the staging folder it may leave is not a story and is removed by the next job and the start-up sweep; a path that
/// would leave the epic folder is refused before the first write; a runner that was fenced publishes nothing.
/// </summary>
public sealed class ABaPublishesAtomicallyTests
{
    /// <summary>The stand-in for a killed process: it is thrown from the boundary hook, and no code of the publisher catches it, so what stands on disk is what a kill would leave.</summary>
    private sealed class Killed(string boundary) : Exception($"killed at {boundary}");

    private static IReadOnlyList<BaFile> Files() =>
    [
        new("stories/s1-first/story.yaml", "story", "id: s1-first\n"),
        new("stories/s1-first/tasks/t1-a/task.yaml", "task", "id: t1-a\n"),
        new("stories/s1-first/tasks/t2-b/task.yaml", "task", "id: t2-b\n"),
        new("stories/s2-second/story.yaml", "story", "id: s2-second\n"),
        new("stories/s2-second/tasks/t1-c/task.yaml", "task", "id: t1-c\n"),
    ];

    private static IReadOnlyList<string> Boundaries { get; } = ["staging-created", .. Enumerable.Range(1, Files().Count).Select(n => $"file-{n}"), "manifest-written", "before-rename", "renamed"];

    public static TheoryData<string> BoundaryNames => [.. Boundaries];

    private static (TempDir Repo, BaEpicSite Site) Site()
    {
        var repo = TempDir.For("coai-ba-publish-");
        BaScenario.Lay(repo);
        var (site, refusal) = BaEpic.Locate(repo, BaScenario.EpicFile);
        refusal.Should().BeEmpty();

        return (repo, site!);
    }

    private static string Stories(BaEpicSite site) => Path.Combine(site.EpicFolder, "stories");

    private static IReadOnlyList<string> StagingFolders(BaEpicSite site) => [.. Directory.GetDirectories(site.EpicFolder, BaEpic.StagingPrefix + "*")];

    [Fact]
    public void ABoundaryNamesList_IsWhatARealPublishReports()
    {
        var (repo, site) = Site();
        using (repo)
        {
            var seen = new List<string>();

            new BaPublisher(site, "job-1", () => true, seen.Add).Publish(Files()).Should().BeOfType<BaPublished.Done>();

            seen.Should().Equal(Boundaries, "the kill test below stops at each of these, so none may be missing");
        }
    }

    [Theory]
    [MemberData(nameof(BoundaryNames))]
    public void KilledAtAnyWriteBoundary_TheEpicHasNoStories_OrTheCompleteSet(string boundary)
    {
        var (repo, site) = Site();
        using (repo)
        {
            var publisher = new BaPublisher(site, "job-1", () => true, name =>
            {
                if (name == boundary)
                {
                    throw new Killed(boundary);
                }
            });

            var act = () => publisher.Publish(Files());

            act.Should().Throw<Killed>();
            if (boundary == "renamed")
            {
                CompleteSet(site).Should().BeTrue("the rename happened: every file and the manifest are in");
            }
            else
            {
                Directory.Exists(Stories(site)).Should().BeFalse($"killed at {boundary}: stories/ was not touched");
                BaEpic.Stories(site.EpicFolder).Should().BeEmpty();
                BaEpic.Locate(site.RepoRoot, BaScenario.EpicFile).Refusal.Should().BeEmpty("a staging folder is not a story: the epic can be planned again");
            }

            // The next BA job for the epic (and the start-up sweep) take away what a kill left.
            BaStaging.Sweep(site.EpicFolder, _ => false);
            StagingFolders(site).Should().BeEmpty();
        }
    }

    private static bool CompleteSet(BaEpicSite site)
    {
        var manifest = Path.Combine(Stories(site), "ba-manifest.json");

        return File.Exists(manifest)
            && Files().All(file => File.Exists(Path.Combine(site.EpicFolder, file.EpicRelative.Replace('/', Path.DirectorySeparatorChar))))
            && JsonDocument.Parse(File.ReadAllText(manifest)).RootElement.GetProperty("files").GetArrayLength() == Files().Count;
    }

    [Fact]
    public void ADonePublish_LeavesTheCompleteSet_AndNoStagingFolder()
    {
        var (repo, site) = Site();
        using (repo)
        {
            var published = new BaPublisher(site, "job-1", () => true).Publish(Files()).Should().BeOfType<BaPublished.Done>().Subject;

            CompleteSet(site).Should().BeTrue();
            StagingFolders(site).Should().BeEmpty();
            published.Manifest.Select(entry => entry.Path).Should().OnlyContain(path => path.StartsWith(BaScenario.EpicFolder + "/stories/", StringComparison.Ordinal));
            published.Manifest.Select(entry => entry.Path).Should().BeInAscendingOrder(StringComparer.Ordinal);
            File.ReadAllText(Path.Combine(Stories(site), "ba-manifest.json")).Should().Be(published.ManifestText);
        }
    }

    [Fact]
    public void AnEmptyStoriesFolder_IsNotAStory_AndIsReplacedByTheMove()
    {
        var (repo, site) = Site();
        using (repo)
        {
            Directory.CreateDirectory(Stories(site));

            new BaPublisher(site, "job-1", () => true).Publish(Files()).Should().BeOfType<BaPublished.Done>();

            CompleteSet(site).Should().BeTrue();
        }
    }

    [Fact]
    public void AStoryThatAppearsBeforeTheRename_IsNeverOverwritten_AndTheStagingIsRemoved()
    {
        var (repo, site) = Site();
        using (repo)
        {
            var publisher = new BaPublisher(site, "job-1", () => true, name =>
            {
                if (name == "before-rename")
                {
                    Directory.CreateDirectory(Path.Combine(Stories(site), "s9-late"));
                }
            });

            publisher.Publish(Files()).Should().BeOfType<BaPublished.Refused>().Which.Reason.Should().Contain("nothing was published");

            Directory.GetDirectories(Stories(site)).Select(Path.GetFileName).Should().Equal("s9-late");
            StagingFolders(site).Should().BeEmpty();
        }
    }

    [Fact]
    public void AnEpicThatAlreadyHasAStory_IsRefusedBeforeTheFirstWrite()
    {
        var (repo, site) = Site();
        using (repo)
        {
            Directory.CreateDirectory(Path.Combine(Stories(site), "s1-there"));

            new BaPublisher(site, "job-1", () => true).Publish(Files()).Should().BeOfType<BaPublished.Refused>().Which.Reason.Should().Contain("has a story now");

            StagingFolders(site).Should().BeEmpty();
        }
    }

    [Fact]
    public void ARunnerThatWasFenced_PublishesNothing_AtTheFirstCompare_OrTheSecond()
    {
        var (repo, site) = Site();
        using (repo)
        {
            new BaPublisher(site, "job-1", () => false).Publish(Files()).Should().BeOfType<BaPublished.Stale>();
            Directory.GetFileSystemEntries(site.EpicFolder).Select(Path.GetFileName).Should().Equal("SPEC.md", "epic.yaml");

            var calls = 0;
            var second = new BaPublisher(site, "job-2", () => ++calls < 2).Publish(Files());

            second.Should().BeOfType<BaPublished.Stale>("the generation is compared again just before the rename");
            Directory.Exists(Stories(site)).Should().BeFalse();
            StagingFolders(site).Should().BeEmpty("a fenced runner takes its own staging away");
        }
    }

    [Theory]
    [InlineData("stories/../../outside/x.yaml")]
    [InlineData("stories/s1-../../../outside/story.yaml")]
    [InlineData("../outside.yaml")]
    [InlineData("/etc/coai-outside.yaml")]
    public void APathThatWouldLeaveTheEpicFolder_IsRefused_BeforeTheFirstWrite(string relative)
    {
        var (repo, site) = Site();
        using (repo)
        {
            var before = BaScenario.Tree(repo);

            var published = new BaPublisher(site, "job-1", () => true).Publish([.. Files(), new BaFile(relative, "task", "x\n")]);

            published.Should().BeOfType<BaPublished.Refused>().Which.Reason.Should().Contain("would leave the epic folder").And.Contain("nothing was written");
            BaScenario.Tree(repo).Should().BeEquivalentTo(before);
            StagingFolders(site).Should().BeEmpty();
        }
    }

    [Fact]
    public void AStoriesFolderThatIsALinkOutOfTheEpic_IsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege there");
        var (repo, site) = Site();
        using (repo)
        using (var outside = TempDir.For("coai-ba-outside-"))
        {
            File.CreateSymbolicLink(Stories(site), outside);

            new BaPublisher(site, "job-1", () => true).Publish(Files()).Should().BeOfType<BaPublished.Refused>().Which.Reason.Should().Contain("would leave the epic folder");

            Directory.GetFileSystemEntries(outside).Should().BeEmpty("nothing reached the folder the link points to");
        }
    }

    [Fact]
    public void TheSecondRunOfOneJob_ReplacesItsOwnStaging()
    {
        var (repo, site) = Site();
        using (repo)
        {
            Directory.CreateDirectory(Path.Combine(site.EpicFolder, BaEpic.StagingPrefix + "job-1", "stories", "s1-stale-half"));

            new BaPublisher(site, "job-1", () => true).Publish(Files()).Should().BeOfType<BaPublished.Done>();

            Directory.GetDirectories(Stories(site)).Select(Path.GetFileName).Should().NotContain("s1-stale-half");
        }
    }

    // ---------- the start-up sweep ----------

    private sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    [Fact]
    public async Task TheStartupSweep_RemovesTheStagingOfAnEndedBaJob_AndLeavesALiveJobsAlone()
    {
        using var world = new RoleJobWorld();
        var (repo, site) = Site();
        using (repo)
        {
            RoleJobRecord Job(bool over)
            {
                var job = world.Store.Create(new RoleJobRequest { JobId = RoleJobIds.New(world.Clock), Role = RoleDescriptors.Ba, RepoPath = repo, TaskFile = BaScenario.EpicFile, TimeoutMinutes = 5 });
                Directory.CreateDirectory(Path.Combine(site.EpicFolder, BaEpic.StagingPrefix + job.JobId, "stories", "s1-half"));
                if (over)
                {
                    world.Store.Change(job.JobId, current => current with { State = RoleJobStates.Failed, Stop = "lost" });
                }

                return job;
            }

            var ended = Job(over: true);
            var running = Job(over: false);
            var service = new RoleJobService(world.DataDir, _ => null, world.Log, _ => true)
            {
                Clock = world.Clock,
                Launcher = new DetachedLauncher(new NothingStarted()),
                Descriptors = [BaScenario.Descriptor()],
                EnvironmentSource = new Hashtable(),
            };

            await service.SweepAsync(TestContext.Current.CancellationToken);

            Directory.Exists(Path.Combine(site.EpicFolder, BaEpic.StagingPrefix + ended.JobId)).Should().BeFalse("its job is over");
            Directory.Exists(Path.Combine(site.EpicFolder, BaEpic.StagingPrefix + running.JobId)).Should().BeTrue("its job may still be writing");
        }
    }
}
