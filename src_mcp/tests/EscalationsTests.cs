using System.Diagnostics;
using System.Text.Json;
using Xunit;
using CoaiMcp.Core.Findings;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Reaching a person through the data directory both halves already use — no port, no token.
/// </summary>
public sealed class EscalationsTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-esc-").FullName;
    private readonly Escalations _escalations;

    public EscalationsTests() => _escalations = new Escalations(_data, pollInterval: TimeSpan.FromMilliseconds(20));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private static EscalationQuestion Question(string id = "q1") => new(
        id,
        "s-1",
        "D:/repo",
        "feature/x",
        "Two findings still gate after three rounds. Ship anyway?",
        "Two findings still gate after three rounds. Ship anyway?",
        "en",
        string.Empty,
        [new Finding(Severity.Blocking, Category.Security, "src/A.cs", 7, "token compared with ==", "timing", "fix it", ["codex"])],
        DateTime.UtcNow.ToString("O"));

    private void WriteAnswer(string id, string text) =>
        File.WriteAllText(
            _escalations.AnswerPath(id),
            JsonSerializer.Serialize(new { id, answer = text, answeredUtc = DateTime.UtcNow.ToString("O") }));

    [Fact]
    public async Task AskHuman_WritesTheQuestionFile_WithItsFindings()
    {
        var task = _escalations.AskAsync(Question(), TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);

        // The file must appear immediately, not when the wait ends.
        var path = _escalations.QuestionPath("q1");
        var appeared = await Task.Run(async () =>
        {
            for (var i = 0; i < 100 && !File.Exists(path); i++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            return File.Exists(path);
        }, TestContext.Current.CancellationToken);
        appeared.Should().BeTrue();

        var written = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).RootElement;
        written.GetProperty("question").GetString().Should().Contain("Ship anyway?");
        written.GetProperty("branch").GetString().Should().Be("feature/x");
        written.GetProperty("openFindings").GetArrayLength().Should().Be(1);

        await task;
    }

    [Fact]
    public async Task AnswerFileAppears_TheToolReturnsIt()
    {
        var task = _escalations.AskAsync(Question(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        WriteAnswer("q1", "no — fix the token comparison first");

        (await task).Should().BeOfType<EscalationOutcome.Answered>()
            .Which.Text.Should().Be("no — fix the token comparison first");
    }

    [Fact]
    public async Task NoAnswerBeforeTheBudget_IsANamedOutcome_NeverASilentProceed()
    {
        var watch = Stopwatch.StartNew();
        var outcome = await _escalations.AskAsync(Question(), TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        watch.Stop();

        outcome.Should().BeOfType<EscalationOutcome.NoAnswerYet>()
            .Which.Waited.Should().Be(TimeSpan.FromMilliseconds(300));
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "it waits the budget, not forever");
        File.Exists(_escalations.QuestionPath("q1")).Should().BeTrue("the question is not withdrawn — a person may still answer");
    }

    [Fact]
    public async Task MalformedAnswerFile_IsIgnored_AndTheWaitContinues()
    {
        // A GENEROUS budget on purpose. This test asserts that the wait is still running after a
        // malformed file appears, and with a five-second budget it was really asserting that the
        // machine got back to it in five seconds — which a loaded one does not. It failed twice
        // during rounds that had six reviewer processes in flight, and passed alone every time.
        var task = _escalations.AskAsync(Question(), TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        File.WriteAllText(_escalations.AnswerPath("q1"), "{ half-written");
        await Task.Delay(100, TestContext.Current.CancellationToken);

        task.IsCompleted.Should().BeFalse("a file that cannot be parsed is not an answer");

        WriteAnswer("q1", "the real answer");
        (await task).Should().BeOfType<EscalationOutcome.Answered>().Which.Text.Should().Be("the real answer");
    }

    [Fact]
    public async Task AnEmptyAnswer_IsNotAnAnswer()
    {
        var task = _escalations.AskAsync(Question(), TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        WriteAnswer("q1", string.Empty);

        (await task).Should().BeOfType<EscalationOutcome.NoAnswerYet>("silence with a file around it is still silence");
    }

    [Fact]
    public async Task TwoConcurrentEscalations_DoNotCrossAnswers()
    {
        var first = _escalations.AskAsync(Question("qA"), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = _escalations.AskAsync(Question("qB"), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        WriteAnswer("qB", "answer for B");
        (await second).Should().BeOfType<EscalationOutcome.Answered>().Which.Text.Should().Be("answer for B");
        first.IsCompleted.Should().BeFalse("A is still open");

        WriteAnswer("qA", "answer for A");
        (await first).Should().BeOfType<EscalationOutcome.Answered>().Which.Text.Should().Be("answer for A");
    }

    [Fact]
    public async Task AnAnswerWrittenDuringTheFinalWait_IsStillSeen()
    {
        // The last poll lands after the deadline; a naive loop would miss this and report silence.
        //
        // The interval is longer than the deadline ON PURPOSE: no intermediate poll can fire, so the
        // only thing that can see this answer is the check AFTER the deadline — which is the whole
        // guarantee. The first shape of this test gave the write 30 ms against a 150 ms deadline and
        // passed on a developer machine while failing on a loaded CI runner, where a `Task.Delay(30)`
        // is not 30 ms; it was measuring the runner, not the loop. Two seconds is not a slower test,
        // it is a margin wide enough that only a missing final check can fail it.
        var slow = new Escalations(_data, pollInterval: TimeSpan.FromSeconds(30));
        var task = slow.AskAsync(Question("qLate"), TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        WriteAnswer("qLate", "just in time");

        (await task).Should().BeOfType<EscalationOutcome.Answered>().Which.Text.Should().Be("just in time");
    }

    [Fact]
    public async Task AKilledServer_LeavesNoLock_AndTheNextAskWorks()
    {
        using var cts = new CancellationTokenSource();
        var abandoned = _escalations.AskAsync(Question("qKilled"), TimeSpan.FromSeconds(30), cts.Token);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        try
        {
            await abandoned;
        }
        catch (OperationCanceledException)
        {
            // the server going away mid-wait
        }

        var task = _escalations.AskAsync(Question("qNext"), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        WriteAnswer("qNext", "still works");

        (await task).Should().BeOfType<EscalationOutcome.Answered>();
    }

    /// <summary>
    /// A <c>call_human</c> notice must survive being the FIRST thing ever written there.
    /// </summary>
    /// <remarks>
    /// <see cref="Escalations.AskAsync"/> creates the directory; <see cref="Escalations.Notify"/>
    /// did not, and its catch swallows <c>DirectoryNotFoundException</c> along with every other
    /// IO failure. On a machine where nobody had used <c>ask_human</c> yet, the verdict that says
    /// "a person must decide" therefore reached nobody, silently — which is the exact failure the
    /// notice was added to end.
    /// </remarks>
    [Fact]
    public void ANotice_IsWritten_EvenWhenNobodyHasEverBeenAskedBefore()
    {
        var fresh = Path.Combine(Directory.CreateTempSubdirectory("coai-esc-fresh-").FullName, "never-used");
        var escalations = new Escalations(fresh);

        escalations.Notify(Question("qFirst"));

        File.Exists(escalations.QuestionPath("qFirst")).Should().BeTrue(
            "a notice nobody can see is the defect, not the cure");
    }

    [Fact]
    public async Task ReadingAnAnswer_DoesNotForbidWritingIt()
    {
        // The defect this family has already paid for three times, in a fourth place: File.ReadAllText
        // opens with FileShare.Read, so a READER forbids writing. Here the reader is the server
        // polling for an answer and the writer is the person answering — and their answer fails with
        // "used by another process" and is lost. Found on 2026-09-05 by a flaky test of this class.
        var path = _escalations.AnswerPath("q1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{}");
        using var stop = new CancellationTokenSource();
        var polling = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                _escalations.ReadAnswer("q1");
            }
        });

        var failures = 0;
        for (var attempt = 0; attempt < 400; attempt++)
        {
            try
            {
                File.WriteAllText(path, "{\"answer\":\"attempt " + attempt + "\"}");
            }
            catch (IOException)
            {
                failures++;
            }
        }

        await stop.CancelAsync();
        await polling;
        failures.Should().Be(0, "a person's answer must not be refused because the server was looking at the file");
    }
}
