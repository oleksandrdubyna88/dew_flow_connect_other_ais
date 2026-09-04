using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;

namespace CoaiMcp.Tests;

/// <summary>
/// Two writers on one session file, which is the ordinary case rather than an exotic one.
/// </summary>
/// <remarks>
/// <para>This machine runs several MCP clients at once, each with a server of its own, and they
/// share a data directory. A round reports progress on every reviewer transition, so a nine-reviewer
/// round saves the session a dozen times while another server is doing the same.</para>
/// <para>Observed in the wild before this test existed, twice in one morning's log:</para>
/// <code>
/// System.UnauthorizedAccessException: Access to the path is denied.
///    at System.IO.FileSystem.MoveFile(String, String, Boolean)
///    at CoaiMcp.Server.LiveRound.Persist()
/// </code>
/// <para>The cause is not the race people expect. The scratch file was named
/// <c>&lt;session&gt;.json.tmp</c> — a FIXED name — so two writers wrote the same scratch path and
/// then both tried to move it. Whoever lost found it gone or held. A per-write name removes the
/// collision; the retry covers the remaining case, a reader or a scanner holding the destination
/// for a moment, which Windows also reports as access denied.</para>
/// </remarks>
// Beside the end-to-end fan-out this is eight threads of pure disk contention on a two-core
// runner, which is how it made a test that spawns real processes fail in a FileStream.
[Collection("fakecli-env")]
public sealed class SessionStoreConcurrencyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-sessions-").FullName;

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

    private static PersistedSession SessionFor(string repo, int rounds) =>
        new(
            new SessionState("abc12345", repo, "main", PanelConfig.Uniform(3, 2)),
            [.. Enumerable.Range(1, rounds).Select(n => new RoundRecord(
                "PlanReview", n, "proceed", 0, "all 3 reviewers answered", DateTime.UtcNow))]);

    [Fact]
    public void EightWritersOnOneSession_DoNotDenyEachOther()
    {
        var store = new SessionStore(_dir);
        var repo = Path.Combine(_dir, "repo");
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 8, i =>
        {
            for (var write = 0; write < 12; write++)
            {
                try
                {
                    store.Save(SessionFor(repo, rounds: i + 1));
                }
                catch (Exception e)
                {
                    failures.Add(e);
                }
            }
        });

        failures.Should().BeEmpty(
            "a session save that throws aborts the round that was reporting its progress");
    }

    [Fact]
    public void AfterTheStorm_TheFileStillParses()
    {
        // A torn write would be worse than a denied one: `Load` answers null for unparseable JSON,
        // so a round's whole trail would silently become a fresh session.
        var store = new SessionStore(_dir);
        var repo = Path.Combine(_dir, "repo2");

        Parallel.For(0, 8, i => store.Save(SessionFor(repo, rounds: i + 1)));

        store.Load(repo, "main").Should().NotBeNull("a half-written file reads as no session at all");
    }

    [Fact]
    public void NoScratchFilesAreLeftBehind()
    {
        var store = new SessionStore(_dir);
        var repo = Path.Combine(_dir, "repo3");

        Parallel.For(0, 8, i => store.Save(SessionFor(repo, rounds: i + 1)));

        Directory.EnumerateFiles(Path.Combine(_dir, "sessions"), "*.tmp*")
            .Should().BeEmpty("a per-write scratch name must still clean up after itself");
    }
}
