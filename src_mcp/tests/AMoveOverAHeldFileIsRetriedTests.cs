using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S24 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c> (E3, C18's product side): a move over a file a
/// reader holds open is refused by Windows with <see cref="UnauthorizedAccessException"/> — never <see cref="IOException"/>
/// (measured 2026-10-20, every share mode of the holder) — and <see cref="AtomicFile"/> retries it inside a short window.
/// </summary>
/// <remarks>
/// Windows only: .NET's Unix file locks are advisory and a rename over a file somebody reads never fails there. The holder
/// opens the file exactly as <c>SharedRead.Text</c> does (<c>FileShare.ReadWrite | FileShare.Delete</c>), the open a real
/// reader of these files makes. The pause is a seam, so no arm sleeps.
/// </remarks>
public sealed class AMoveOverAHeldFileIsRetriedTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-atomic-held-");

    public void Dispose() => _dir.Dispose();

    private string Seeded(string text)
    {
        var path = _dir.At("record.json");
        File.WriteAllText(path, text);

        return path;
    }

    private static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static void OnWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "the refusal of a move over a held file is Windows'; .NET's Unix locks are advisory");

    private string[] Temps() => [.. Directory.EnumerateFiles(_dir.Path, "*.tmp")];

    [Fact]
    public void AMoveOverAFileAReaderHolds_IsRetriedWithinTheWindow_AndLands()
    {
        OnWindows();
        var path = Seeded("old");
        var holder = Hold(path);
        var pauses = 0;
        var seams = new AtomicSeams
        {
            Pause = _ =>
            {
                pauses++;
                holder.Dispose();
            },
        };

        var write = () => AtomicFile.Write(path, "new", TimeSpan.FromSeconds(5), seams);

        write.Should().NotThrow("the reader lets go after one pause, well inside the window");
        File.ReadAllText(path).Should().Be("new");
        pauses.Should().BeGreaterThanOrEqualTo(1, "the first move was refused, so the writer paused and tried again");
        Temps().Should().BeEmpty();
    }

    [Fact]
    public void AHolderThatNeverLetsGo_FailsTheWriteWhenTheWindowEnds_AndTheOriginalSurvives_WithNoTemp()
    {
        OnWindows();
        var path = Seeded("old");
        using var holder = Hold(path);
        var clock = new MovableClock(DateTimeOffset.UnixEpoch);
        var pauses = 0;
        var seams = new AtomicSeams
        {
            Clock = clock,
            Pause = _ =>
            {
                pauses++;
                clock.Advance(TimeSpan.FromMilliseconds(50));
            },
        };

        var write = () => AtomicFile.Write(path, "new", TimeSpan.FromMilliseconds(200), seams);

        write.Should().Throw<UnauthorizedAccessException>("a persistent refusal is the caller's to see — a retry is not a way round an ACL");
        pauses.Should().BeInRange(3, 5, "the window is 200 ms of the MONOTONIC clock and each pause moves it 50 ms");
        holder.Dispose();
        File.ReadAllText(path).Should().Be("old", "the original bytes survive a write that never landed");
        Temps().Should().BeEmpty("the failed write removes its own temp file");
    }

    [Fact]
    public void AZeroWindow_TriesOnce_AndNeverPauses()
    {
        OnWindows();
        var path = Seeded("old");
        using var holder = Hold(path);
        var pauses = 0;
        var seams = new AtomicSeams { Pause = _ => pauses++ };

        var write = () => AtomicFile.Write(path, "new", default, seams);

        write.Should().Throw<UnauthorizedAccessException>();
        pauses.Should().Be(0, "no window, one try");
    }
}
