using System.Collections.Immutable;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Captures what was logged, so the audit trail is asserted rather than eyeballed.</summary>
internal sealed class ListSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];

    public void Emit(LogEvent logEvent)
    {
        lock (_events)
        {
            _events.Add(logEvent);
        }
    }

    /// <summary>
    /// Rendered the way the real sinks render — <c>{Message:lj}</c> through
    /// <see cref="Serilog.Formatting.Display.MessageTemplateTextFormatter"/>.
    /// </summary>
    /// <remarks>
    /// Not <c>RenderMessage()</c>: that quotes every string property, so an asserted path arrives
    /// as <c>"src/TokenGate.cs":23</c> and the test would be measuring an artefact of itself
    /// rather than the line that lands in the file.
    /// </remarks>
    private static readonly Serilog.Formatting.Display.MessageTemplateTextFormatter Formatter =
        new("{Message:lj}");

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_events)
            {
                return [.. _events.Select(Render)];
            }
        }
    }

    private static string Render(LogEvent e)
    {
        var writer = new StringWriter();
        Formatter.Format(e, writer);
        return writer.ToString();
    }

    /// <summary>The raw events, for a test that needs to format one itself.</summary>
    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public IReadOnlyList<LogEventLevel> Levels
    {
        get
        {
            lock (_events)
            {
                return [.. _events.Select(e => e.Level)];
            }
        }
    }

    public string All => string.Join('\n', Lines);
}

/// <summary>
/// The audit trail exists to answer one question a summary sentence cannot: WHY a reviewer did
/// not review. These tests hold that promise.
/// </summary>
public sealed class RoundAuditTests
{
    private readonly ListSink _sink = new();
    private readonly ILogger _log;

    public RoundAuditTests() =>
        _log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_sink).CreateLogger();

    private static ReviewerWork Work(string provider, ReviewRole role, params string[] args) =>
        new(new ReviewerInvocation(
            provider,
            role,
            new ProcessRequest("codex", args, "D:/wt") { StdIn = "the prompt" }));

    [Fact]
    public void AFailedReviewer_IsAWarningNamingTheReason()
    {
        var audit = new RoundAudit(_log, "PlanReview", 2);

        audit.Moved(new ReviewerProgress(
            "codex",
            ReviewRole.PlanCritique,
            ReviewerState.Failed,
            new ReviewerOutcome.NonZeroExit(1, "stream error: the frobnicator is out of widgets"),
            TimeSpan.FromSeconds(42)));

        _sink.All.Should().Contain("codex").And.Contain("frobnicator is out of widgets").And.Contain("42");
        _sink.Levels.Should().ContainSingle().Which.Should().Be(
            LogEventLevel.Warning, "a reviewer that did not review is what someone will search this log for");
    }

    [Fact]
    public void AnAnsweringReviewer_RecordsItsTokensAndItsCost()
    {
        var audit = new RoundAudit(_log, "CodeReview", 1);
        var review = new NormalisedReview([], []);

        audit.Moved(new ReviewerProgress(
            "claude",
            ReviewRole.SecurityReliability,
            ReviewerState.Done,
            new ReviewerOutcome.Ok(review, Repaired: false, new Usage(14200, 15, 0.0489)),
            TimeSpan.FromSeconds(63.4)));

        _sink.All.Should().Contain("14200").And.Contain("15").And.Contain("$0.0489");
    }

    [Fact]
    public void TheOpeningLine_NamesEveryReviewerAndTheArgvBehindIt()
    {
        var audit = new RoundAudit(_log, "CodeReview", 1);

        audit.Opening(
            [Work("codex", ReviewRole.Architecture, "exec", "--json"), Work("gemini", ReviewRole.Architecture, "-p")],
            "D:/wt",
            TimeSpan.FromMinutes(6));

        // The roster at Information, the exact command at Debug — the difference between knowing
        // codex was asked and being able to reproduce the failure in a terminal.
        _sink.All.Should().Contain("codex/Architecture").And.Contain("gemini/Architecture").And.Contain("D:/wt");
        _sink.All.Should().Contain("--json", "the argv is what makes a failure reproducible by hand");
    }

    [Fact]
    public void EveryFinding_IsRecordedWithItsOriginOnDisk()
    {
        var audit = new RoundAudit(_log, "CodeReview", 1);
        var finding = new Finding(
            Severity.Blocking, Category.Security, "src/TokenGate.cs", 23,
            "Remote response parsing accepts invalid responses as valid",
            "Contains(\"valid\") matches \"invalid\"", "compare the exact status", ["codex", "claude"]);

        audit.Findings([finding]);

        _sink.All.Should().Contain("src/TokenGate.cs:23").And.Contain("codex+claude").And.Contain("[gating]");
    }
}
