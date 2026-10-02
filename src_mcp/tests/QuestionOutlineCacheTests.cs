using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The outline cache (S4b item 11): one build per repository + HEAD, the last four kept, the least recently asked out
/// first, and a build that failed never kept.
/// </summary>
public sealed class QuestionOutlineCacheTests
{
    private int _builds;

    private Func<Task<string>> Build(string outline) => () =>
    {
        Interlocked.Increment(ref _builds);

        return Task.FromResult(outline);
    };

    [Fact]
    public async Task OnePairIsBuiltOnce_AndTwoSpellingsOfOneRepositoryAreOnePair()
    {
        var cache = new QuestionOutlineCache();

        (await cache.GetAsync("D:/rsd/Shop", "abc123", Build("outline"))).Should().Be("outline");
        (await cache.GetAsync(@"d:\rsd\shop\", "abc123", Build("another"))).Should().Be("outline", "the same checkout, spelled the other way");

        _builds.Should().Be(1);
    }

    [Fact]
    public async Task TheLastFourAreKept_TheLeastRecentlyAskedGoesFirst()
    {
        var cache = new QuestionOutlineCache();
        foreach (var head in (string[])["h1", "h2", "h3", "h4"])
        {
            await cache.GetAsync("D:/repo", head, Build(head));
        }

        await cache.GetAsync("D:/repo", "h1", Build("h1 again")); // h1 is now the most recent; h2 the least
        await cache.GetAsync("D:/repo", "h5", Build("h5"));

        cache.Count.Should().Be(QuestionOutlineCache.Capacity);
        _builds.Should().Be(5);
        (await cache.GetAsync("D:/repo", "h1", Build("rebuilt"))).Should().Be("h1", "h1 was asked recently, so it stayed");
        (await cache.GetAsync("D:/repo", "h2", Build("rebuilt"))).Should().Be("rebuilt", "h2 was the least recently asked, so it went");
    }

    [Fact]
    public async Task AnEmptyOrAFailedBuild_IsNotKept_SoTheNextQuestionTriesAgain()
    {
        var cache = new QuestionOutlineCache();

        (await cache.GetAsync("D:/repo", "h1", Build(string.Empty))).Should().BeEmpty();
        var failed = () => cache.GetAsync("D:/repo", "h2", () => Task.FromException<string>(new IOException("git refused")));
        await failed.Should().ThrowAsync<IOException>();

        (await cache.GetAsync("D:/repo", "h1", Build("built"))).Should().Be("built");
        (await cache.GetAsync("D:/repo", "h2", Build("built too"))).Should().Be("built too");
    }

    [Fact]
    public async Task TwoQuestionsAskingWhileItBuilds_ShareTheOneBuild()
    {
        var cache = new QuestionOutlineCache();
        var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var builds = 0;
        Task<string> Slow()
        {
            Interlocked.Increment(ref builds);

            return gate.Task;
        }

        var first = cache.GetAsync("D:/repo", "h1", Slow);
        var second = cache.GetAsync("D:/repo", "h1", Slow);
        gate.SetResult("outline");

        (await first).Should().Be("outline");
        (await second).Should().Be("outline");
        builds.Should().Be(1);
    }
}
