using CoaiMcp.Core.Notices;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A poll that meets a file another process is still writing waits for the next poll — issue #512.
/// </summary>
/// <remarks>
/// <c>ARunThatDiesIsRecorded</c> failed the mcp-v0.33.0 release on win-x64, and once more locally under
/// the full suite: its poll read <c>server-notices.jsonl</c> while a live server held it open to append,
/// Windows refused the read with a sharing violation, and one unlucky poll failed the whole test instead
/// of waiting for the next one. These pin the two halves of the fix without a server: the poll treats a
/// busy file as "not yet", and the reader treats a half-written last line the same way.
/// </remarks>
public sealed class APollWaitsOutABusyFileTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _dir = Directory.CreateTempSubdirectory("coai-busy-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory that outlives one run is litter, not a failed test.
        }
    }

    private static string Refusal(string detail) =>
        $$"""{"code":"{{ServerNoticeCodes.Refused}}","detail":"{{detail}}"}""";

    [Fact]
    public async Task AFileHeldByAnotherHandle_IsNotYet_AndThePollFindsItOnceReleased()
    {
        var file = Path.Combine(_dir, "held.jsonl");
        await File.WriteAllTextAsync(file, Refusal("one") + "\n", TestContext.Current.CancellationToken);
        var held = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var releasing = Task.Run(async () =>
        {
            await Task.Delay(300, TestContext.Current.CancellationToken);
            await held.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        var found = await Polls.Until(() => File.ReadAllLines(file).Length > 0, Patience);
        await releasing;

        found.Should().BeTrue("a busy file is 'not yet', and the poll after the release reads it");
    }

    [Fact]
    public async Task AConditionThatIsRefusedAccess_IsNotYet_Either()
    {
        var polls = 0;

        var found = await Polls.Until(
            () => ++polls >= 3 ? true : throw new UnauthorizedAccessException("being replaced underneath"), Patience);

        found.Should().BeTrue("a file caught mid-replace is refused access, which is 'not yet' as a busy one is");
    }

    [Fact]
    public async Task AConditionThatNeverComesTrue_StillAnswersFalse_AtTheDeadline()
    {
        var found = await Polls.Until(() => throw new IOException("always busy"), TimeSpan.FromMilliseconds(300));

        found.Should().BeFalse("catching the busy read must not turn a poll that never succeeded into a pass");
    }

    [Fact]
    public async Task AHalfWrittenLastLine_IsNotYet_AndTheWholeOnesAreRead()
    {
        var file = Path.Combine(_dir, "notices.jsonl");
        await File.WriteAllTextAsync(
            file, Refusal("one") + "\n" + Refusal("two") + "\n" + """{"code":"refu""", TestContext.Current.CancellationToken);

        var notices = ARunThatDiesIsRecordedTests.NoticesIn(file, ServerNoticeCodes.Refused);

        notices.Should().HaveCount(2, "the tail a writer is still appending is not a notice yet");
    }

    [Fact]
    public async Task AMalformedCompleteLine_IsStillAFailure_NotSilentlySkipped()
    {
        var file = Path.Combine(_dir, "corrupt.jsonl");
        await File.WriteAllTextAsync(file, "not json\n" + Refusal("one") + "\n", TestContext.Current.CancellationToken);

        var reading = () => ARunThatDiesIsRecordedTests.NoticesIn(file, ServerNoticeCodes.Refused);

        reading.Should().Throw<System.Text.Json.JsonException>(
            "a terminated line that is not JSON is a defect in the writer, and hiding it would hide that");
    }

    [Fact]
    public async Task ANoticesFileAWriterHoldsOpen_IsReadWithoutAWait()
    {
        var file = Path.Combine(_dir, "appending.jsonl");
        await File.WriteAllTextAsync(file, Refusal("one") + "\n", TestContext.Current.CancellationToken);
        // How the server holds it: open to append, letting others read and write.
        await using var writer = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        var reading = () => ARunThatDiesIsRecordedTests.NoticesIn(file, ServerNoticeCodes.Refused);

        reading.Should().NotThrow("a reader must not forbid the writing that is going on").Which.Should().HaveCount(1);
    }
}
