using System.Reflection;
using Xunit;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// E2.S1 §S1.5 — which earlier round a delta is measured from, or why none: pure, over the trail the round engine
/// hands the work. Unless a row says otherwise every code record carries a <c>Size</c> measured against
/// <c>base0</c> and <c>Choose</c> is asked with <c>against: "base0"</c>, so most rows test the selection and the last
/// few test the base.
/// </summary>
public sealed class TheDeltaIsMeasuredFromTheLastReviewedHeadTests
{
    private const string Base = "base0";

    private static RoundSize SizeAgainst(string from) =>
        new(new ScopeSize(ScopeState.Recorded, from, []), ScopeSize.ObservedZero(string.Empty));

    private static RoundRecord Round(Stage stage, int number, string sha, string status = RoundRecord.Done, string verdict = "proceed", string baseRef = Base) =>
        new(stage.ToString(), number, verdict, 0, "codex", new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc))
        {
            Status = status,
            Sha = sha,
            Size = SizeAgainst(baseRef),
        };

    private static RoundRecord Code(int number, string sha, string status = RoundRecord.Done, string baseRef = Base) =>
        Round(Stage.CodeReview, number, sha, status, baseRef: baseRef);

    /// <summary>A skipped round, built exactly as RoundSkips.cs:102-111 builds it: done, WITH a sha, reviewed nothing.</summary>
    private static RoundRecord Skipped(int number, string sha) =>
        new(Stage.CodeReview.ToString(), number, RoundRecord.Skipped, 0, "nobody ran", DateTime.UtcNow)
        {
            Status = RoundRecord.Done,
            StartedUtc = DateTime.UtcNow,
            Sha = sha,
            Note = "nobody ticked",
        };

    private static Predecessor Choose(params RoundRecord[] trail) => DeltaPredecessor.Choose(trail, Base);

    [Fact]
    public void NoEarlierCodeRound_IsTheFirstRound()
    {
        Choose().Should().Be(Predecessor.FirstRound);
        Choose(Round(Stage.PlanReview, 1, string.Empty)).Should().Be(Predecessor.FirstRound);
        Choose(Round(Stage.PlanReview, 1, string.Empty), Round(Stage.PlanReview, 2, string.Empty)).Should().Be(Predecessor.FirstRound);
        Predecessor.FirstRound.State.Should().Be(ScopeState.FirstRound);
        Predecessor.FirstRound.IsChosen.Should().BeFalse();
    }

    [Fact]
    public void TheLatestDoneCodeRoundWithASha_IsThePredecessor()
    {
        var chosen = Choose(Code(1, "A"), Code(2, "B"));

        chosen.IsChosen.Should().BeTrue();
        chosen.Sha.Should().Be("B");
        chosen.Should().Be(Predecessor.Of("B"));
    }

    [Fact]
    public void ASkippedRoundIsNeverThePredecessor_ThoughItIsDoneWithASha() =>
        Choose(Code(1, "A"), Skipped(2, "B")).Should().Be(Predecessor.Of("A"));

    [Fact]
    public void ARunningOrInterruptedRound_IsNeverThePredecessor() =>
        Choose(Code(1, "A"), Code(2, "B", RoundRecord.Interrupted), Code(3, "C", RoundRecord.Running))
            .Should().Be(Predecessor.Of("A"));

    [Fact]
    public void APlanDocumentOrFeatureRound_IsNeverThePredecessor() =>
        Choose(Code(1, "A"), Round(Stage.FeatureReview, 1, "F"), Round(Stage.DocumentReview, 1, "D"))
            .Should().Be(Predecessor.Of("A"));

    [Fact]
    public void ARoundWhoseEveryReviewerFailed_StillQualifies()
    {
        var failed = Code(2, "B") with { Verdict = "call_human" };
        failed.ReviewerStates.AddRange(
        [
            new ReviewerState("codex", "code", ReviewerState.Failed),
            new ReviewerState("gemini", "code", ReviewerState.Failed),
        ]);

        Choose(Code(1, "A"), failed).Should().Be(Predecessor.Of("B"));
    }

    [Fact]
    public void OnlyPreShaRounds_IsNoPredecessorSha()
    {
        Choose(Code(1, string.Empty)).Should().Be(Predecessor.NoSha);
        Choose(Round(Stage.PlanReview, 1, string.Empty), Code(1, string.Empty)).Should().Be(Predecessor.NoSha);
        Predecessor.NoSha.State.Should().Be(ScopeState.NoPredecessorSha);
        Predecessor.NoSha.IsChosen.Should().BeFalse();
    }

    [Fact]
    public void TheLatestRoundWithAShaWins_OverANewerOneWithout()
    {
        Choose(Code(1, "A"), Code(2, string.Empty)).Should().Be(Predecessor.Of("A"));
        Choose(Code(1, string.Empty), Code(2, "B")).Should().Be(Predecessor.Of("B"));
    }

    [Fact]
    public void ListOrderDecides_NotTheClock()
    {
        var first = Code(1, "A") with { CompletedUtc = new DateTime(2026, 10, 15, 18, 0, 0, DateTimeKind.Utc) };
        var second = Code(2, "B") with { CompletedUtc = new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc) };

        Choose(first, second).Should().Be(Predecessor.Of("B"));
    }

    [Fact]
    public void TheStageIsMatchedByItsPersistedName_Ordinally()
    {
        var lowercase = Code(1, "A") with { Stage = "codereview" };
        var exact = Code(1, "A") with { Stage = "CodeReview" };

        Choose(lowercase).Should().Be(Predecessor.FirstRound);
        Choose(exact).Should().Be(Predecessor.Of("A"));
    }

    [Fact]
    public void APredecessorWithoutASize_IsBaseUnknown_WithItsShaKept()
    {
        var unsized = Code(1, "A") with { Size = null };
        var emptyFrom = Code(1, "A") with { Size = SizeAgainst(string.Empty) };

        Choose(unsized).Should().Be(Predecessor.Refused("A", ScopeState.BaseUnknown));
        Choose(emptyFrom).Should().Be(Predecessor.Refused("A", ScopeState.BaseUnknown));
        Choose(unsized).IsChosen.Should().BeFalse();
    }

    [Fact]
    public void APredecessorMeasuredAgainstAnotherBase_IsBaseMoved()
    {
        var trail = new[] { Code(1, "A") };

        DeltaPredecessor.Choose(trail, "base1").Should().Be(Predecessor.Refused("A", ScopeState.BaseMoved));
        DeltaPredecessor.Choose(trail, "BASE0").Should().Be(Predecessor.Of("A"), "the head comparison is case-insensitive");
    }

    [Fact]
    public void TheBaseIsAskedOfTheChosenRoundOnly() =>
        Choose(Code(1, "A", baseRef: "old"), Code(2, "B", baseRef: Base)).Should().Be(Predecessor.Of("B"));

    /// <summary>
    /// The criterion-7 tripwire: the delta is a range, never arithmetic — so no unit here accepts two sizes. Only
    /// INPUTS count (an <c>out</c> parameter is how a record deconstructs, not a second size to subtract).
    /// </summary>
    [Fact]
    public void NoSizeUnitTakesTwoSizes()
    {
        Type[] units = [typeof(ChangePartition), typeof(DeltaPredecessor), typeof(ScopeSize), typeof(RoundSize), typeof(SizeCell)];
        var sizeTypes = new[] { typeof(ScopeSize), typeof(RoundSize), typeof(SizeCell), typeof(IEnumerable<SizeCell>) };
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var offenders = units
            .SelectMany(u => u.GetMethods(all).Cast<MethodBase>().Concat(u.GetConstructors(all)))
            // Constructors are always IsSpecialName, so the filter names the compiler's operators (op_Equality ...) instead.
            .Where(m => !m.IsPrivate && !m.Name.StartsWith("op_", StringComparison.Ordinal) && !IsRoundSizesOwnPrimaryConstructor(m))
            .Where(m => m.GetParameters().Count(p => !p.ParameterType.IsByRef && sizeTypes.Any(t => t.IsAssignableFrom(p.ParameterType))) > 1)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        offenders.Should().BeEmpty("a delta is a head-to-head range; nothing in this unit may subtract two sizes");
    }

    /// <summary>The one constructor allowed two sizes: <c>RoundSize(Whole, Delta, Origin)</c>, which holds both scopes side by side and subtracts neither.</summary>
    private static bool IsRoundSizesOwnPrimaryConstructor(MethodBase m) =>
        m is ConstructorInfo && m.DeclaringType == typeof(RoundSize)
        && m.GetParameters().Select(p => p.Name).SequenceEqual(["Whole", "Delta", "Origin"]);
}
