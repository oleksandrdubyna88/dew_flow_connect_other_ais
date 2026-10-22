using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// D11 of <c>todo/PLAN_a_cancelled_review_stops_its_round.md</c>: a withdrawal stays a withdrawal whatever is thrown after the decision. The save of its
/// record is logged when the disk refuses it — whichever exception the refusal arrives as — and the caller is still owed a cancellation, never "the round failed".
/// </summary>
public sealed class AWithdrawalsSaveFailureIsStillACancellationTests
{
    private sealed class Lines : Serilog.Core.ILogEventSink
    {
        public List<string> Rendered { get; } = [];

        public void Emit(Serilog.Events.LogEvent logEvent) => Rendered.Add(logEvent.RenderMessage());
    }

    private static (Serilog.ILogger Log, Lines Lines) Logger()
    {
        var lines = new Lines();

        return (new Serilog.LoggerConfiguration().WriteTo.Sink(lines).CreateLogger(), lines);
    }

    public static TheoryData<Exception> RefusalsOfTheDisk() =>
        new()
        {
            new SessionStoreException("the session could not be written", new IOException("disk full")),
            new IOException("the disk is full"),
            new UnauthorizedAccessException("the data directory is read-only"),
        };

    [Theory]
    [MemberData(nameof(RefusalsOfTheDisk))]
    public void ASaveTheDiskRefuses_IsLogged_AndTheWithdrawalGoesOn(Exception refusal)
    {
        var (log, lines) = Logger();

        var landed = RoundEngine.Recorded(() => throw refusal, log, 3);

        landed.Should().BeFalse();
        lines.Rendered.Should().ContainSingle(l => l.Contains("the withdrawal of round 3 could not be recorded", StringComparison.Ordinal));
    }

    [Fact]
    public void ASaveThatLanded_SaysSo_AndLogsNothing()
    {
        var (log, lines) = Logger();

        RoundEngine.Recorded(() => { }, log, 1).Should().BeTrue();
        lines.Rendered.Should().BeEmpty();
    }

    [Fact]
    public void ADefect_IsNotSwallowedAsAFullDisk()
    {
        var (log, _) = Logger();

        var act = () => RoundEngine.Recorded(() => throw new InvalidOperationException("a bug"), log, 1);

        act.Should().Throw<InvalidOperationException>("only a refusal of the disk is answered as a cancellation; a defect is not hidden");
    }
}
