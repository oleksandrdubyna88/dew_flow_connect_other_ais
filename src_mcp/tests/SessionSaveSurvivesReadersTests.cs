using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;

namespace CoaiMcp.Tests;

/// <summary>
/// A reader must not be able to kill a round, and a failed repaint must not be able to either.
/// </summary>
/// <remarks>
/// <para><b>What actually happened.</b> The gate killed six of its own code rounds with
/// <c>Access to the path is denied</c>. One of the six died on the FINAL save, after every reviewer
/// had answered — the findings were in memory and were thrown away for the sake of a file name
/// nobody was ever told.</para>
/// <para>Two causes, and the first is the one nobody looks for: <c>File.ReadAllText</c> opens with
/// <c>FileShare.Read</c>, so a READER forbids writing. Five <c>coai-mcp</c> processes were alive on
/// this machine — one per VS Code window — each polling the sessions directory, so a writer's
/// <c>File.Move</c> landed on a file somebody was reading and failed.</para>
/// <para>The second is that it failed as <c>UnauthorizedAccessException</c>, which is NOT an
/// <c>IOException</c> — so <c>LiveRound.Persist</c>'s <c>catch (IOException)</c>, written for exactly
/// this case, walked straight past it and took the round down.</para>
/// </remarks>
public sealed class SessionSaveSurvivesReadersTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-readers-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PersistedSession SessionFor(string repo, int rounds) =>
        new(
            new SessionState("abc12345", repo, "main", PanelConfig.Uniform(3, 2)),
            [.. Enumerable.Range(1, rounds).Select(n => new RoundRecord(
                "PlanReview", n, "proceed", 0, "all 3 reviewers answered", DateTime.UtcNow))]);

    [Fact]
    public async Task AReaderPollingTheSessionCannotStopAWriter()
    {
        // One writer and four readers, which is one VS Code window running a round and four others
        // watching. Every one of the five is this product.
        var store = new SessionStore(_dir);
        var repo = Path.Combine(_dir, "repo");
        store.Save(SessionFor(repo, 1));
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    store.Load(repo, "main");
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            }
        })).ToArray();

        for (var write = 0; write < 60 && !stop.IsCancellationRequested; write++)
        {
            try
            {
                store.Save(SessionFor(repo, write + 1));
            }
            catch (Exception e)
            {
                failures.Add(e);
            }
        }

        await stop.CancelAsync();
        await Task.WhenAll(readers);
        failures.Should().BeEmpty("a window that is merely LOOKING at the rounds must not end a round");
    }

    [Fact]
    public void ASaveThatCannotLand_FailsAsItselfRatherThanAsSomeoneElsesException()
    {
        // Named, so a caller can decide what to do about it. Before this, the failure arrived as
        // UnauthorizedAccessException — which is not an IOException, so the one catch written for
        // this case did not see it.
        var asFile = Path.Combine(_dir, "not-a-dir");
        File.WriteAllText(asFile, "the sessions directory cannot be created under a file");
        var store = new SessionStore(asFile);

        var act = () => store.Save(SessionFor("C:/repo", 1));

        act.Should().Throw<SessionStoreException>();
    }

    [Fact]
    public void AFailedRepaint_DoesNotTakeTheRoundDown()
    {
        // The repaint is the one save that is allowed to be lost: the next progress event writes
        // again, and a missed frame is not a failed review. This is the catch that was too narrow.
        var asFile = Path.Combine(_dir, "not-a-dir-either");
        File.WriteAllText(asFile, "no sessions directory here either");
        var store = new SessionStore(asFile);

        var act = () => new LiveRound(store, SessionFor("C:/repo", 1), [], "a subject");

        act.Should().NotThrow("a round that cannot draw itself is still a round");
    }
}
