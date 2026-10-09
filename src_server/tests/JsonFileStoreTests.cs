using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The one file store, and what a reader holding a file does to a write over it.
/// </summary>
/// <remarks>
/// <see cref="SessionStore.Active"/> reads every session file about once a minute per open admin page,
/// and <see cref="SessionStore.Validate"/> replaces a session file once an hour per session with
/// <c>File.Move(…, overwrite: true)</c>. On Windows that replace is refused while anybody holds the
/// target open. Found by the own review of the people endpoint, 2026-10-09, before it could be seen as
/// a 500; the fix is <see cref="SessionStore.Validate"/>'s non-fatal stamp (<c>SessionTests</c>), and the
/// first test here keeps the approach that was tried first and measured not to work.
/// </remarks>
public sealed class JsonFileStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static SessionRecord Record(string name) => new("dev@example.com", name, Now, Now.AddDays(7), Now);

    /// <summary>
    /// The obvious fix — let the reader grant every share, delete included — was measured and refuted:
    /// <c>File.Move(overwrite: true)</c> goes through <c>MoveFileEx</c>, which has no POSIX rename, and
    /// the replace is refused whatever the reader granted. So the reader keeps its default share and
    /// the colliding WRITE is made non-fatal instead. If this test ever passes, the runtime changed and
    /// the share-side fix is back on the table.
    /// </summary>
    [Fact]
    public void AWiderReadShare_WouldNotLetTheReplaceThrough_WhichIsWhyTheStampIsNonFatalInstead()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to replace a file somebody holds open; Linux lets the rename through");
        var path = Path.Combine(Directory.CreateTempSubdirectory("coai-store-").FullName, "record.json");
        var store = new JsonFileStore();
        store.Write(path, Record("before"), ServerJsonContext.Default.SessionRecord);

        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var replace = () => store.Write(path, Record("after"), ServerJsonContext.Default.SessionRecord);

        replace.Should().Throw<Exception>("measured 2026-10-09: the widest share a reader can grant does not let MoveFileEx replace the file")
            .Which.Should().Match(e => e is UnauthorizedAccessException || e is IOException);
        JsonFileStore.ReadShare.Should().Be(FileShare.Read, "widening it buys nothing, so Read keeps the default a ReadAllText has");
    }

    [Fact]
    public void AFileReadThroughTheStore_RoundTrips()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("coai-store-").FullName, "record.json");
        var store = new JsonFileStore();
        store.Write(path, Record("A Developer"), ServerJsonContext.Default.SessionRecord);

        store.Read(path, ServerJsonContext.Default.SessionRecord).Should().Be(Record("A Developer"));
    }

    [Fact]
    public void AnAbsentFile_IsNullAndIsNotReported()
    {
        var reported = new List<string>();
        var store = new JsonFileStore((message, _) => reported.Add(message));

        store.Read(Path.Combine(Path.GetTempPath(), "coai-store-never", "absent.json"), ServerJsonContext.Default.SessionRecord)
            .Should().BeNull();
        reported.Should().BeEmpty("absent and unreadable are different facts, and only the second is a finding");
    }
}

/// <summary>
/// The hourly sweep that keeps <c>sessions/</c> to the sessions that are alive, on a server that is
/// never restarted.
/// </summary>
public sealed class SessionSweeperTests
{
    /// <summary>Long enough to be sure the answer is not coming, short enough to fail fast.</summary>
    private static readonly TimeSpan Promptly = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task TheSweeper_RemovesAnExpiredSession_OnItsOwnClock()
    {
        var data = Directory.CreateTempSubdirectory("coai-sweeper-").FullName;
        var store = new SessionStore(data, TimeSpan.FromDays(7));
        var now = DateTimeOffset.UtcNow;
        var (live, _) = store.Issue($"new@{TeamServer.Domain}", "", now);
        var sessions = Path.Combine(data, "sessions");
        using var sweeper = new SessionSweeper(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionSweeper>.Instance, Period);
        await sweeper.StartAsync(TestContext.Current.CancellationToken);

        // Written AFTER the sweeper started, so only a tick — never a boot sweep — can remove it.
        store.Issue($"old@{TeamServer.Domain}", "", now.AddDays(-30));
        var deadline = DateTimeOffset.UtcNow + Promptly;
        while (Directory.GetFiles(sessions, "*.json").Length > 1 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(Period, TestContext.Current.CancellationToken);
        }

        await sweeper.StopAsync(TestContext.Current.CancellationToken);

        Directory.GetFiles(sessions, "*.json").Should().ContainSingle("the expired one is gone and the living one is not");
        store.Validate(live, DateTimeOffset.UtcNow).Should().NotBeNull();
    }

    [Fact]
    public void TheSweeperIsAnHourlyClock_ByDefault() =>
        SessionSweeper.DefaultPeriod.Should().Be(TimeSpan.FromHours(1));

    [Collection(ServerCollection.Name)]
    public sealed class Wiring
    {
        /// <summary>
        /// A sweep fully implemented and invoked by nothing is the reliability audit's most instructive
        /// find; this pins that the host actually runs it.
        /// </summary>
        [Fact]
        public async Task TheServer_RunsTheSweeper()
        {
            using var server = new TeamServer();
            // The host starts on the first client.
            using var client = server.CreateClient();
            await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

            server.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .Should().Contain(s => s is SessionSweeper, "a sweep nobody invokes is not a sweep");
        }
    }
}
