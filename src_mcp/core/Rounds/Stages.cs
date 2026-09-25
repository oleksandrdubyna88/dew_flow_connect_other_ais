using System.Collections.Frozen;
using System.Collections.Immutable;
using CoaiMcp.Core.Commands;

namespace CoaiMcp.Core.Rounds;

/// <summary>
/// Everything one <see cref="Stage"/> answers by name — its roster, its phrase, its next stage, its
/// sentences — in one row.
/// </summary>
/// <param name="Bucket">Which roles review it: the ONE bridge to the string a role is persisted with.</param>
/// <param name="Phrase">The stage as a person would say it ("plan review"), for a notice or a log line.</param>
/// <param name="Kind">The adjective in "every code-review role": one word inside a refusal.</param>
/// <param name="Commands">Which of a person's custom commands a round of this stage is given.</param>
/// <param name="AdvancesTo">Where <c>resolve</c> moves the session once a round of this stage passed.</param>
/// <param name="CompletedSentence">What <c>resolve</c> says at that moment — the caller's next move.</param>
/// <param name="ReviseInstruction">
/// What a <c>revise</c> verdict tells the caller to do with the accepted findings before the next
/// round. The same words for every stage that exists today; a stage whose fixes land elsewhere
/// (as new pull requests, say) writes its own.
/// </param>
/// <param name="RecordsSha">
/// Whether a round of this stage keeps the commit it reviewed, which is what a later <c>again</c>
/// compares the branch to. A round over no commit — a plan, a document — records none, and an
/// empty one never refuses.
/// </param>
/// <remarks>
/// <para>Four switches used to hold these facts apart — a bucket here, a phrase there, a command
/// stage in the panel service, a sentence in <c>Finish</c> — and two of them were exhaustive while
/// four swallowed an unknown stage with a discard: a document review's notice read "The done gate
/// needs your decision", its <c>resolve</c> said "The code stage is complete", and a fourth stage
/// would have inherited every one of those silently. One table, and a test that walks the enum,
/// is what makes adding a stage a compile-time question rather than a reading of six files.</para>
/// <para>Only the fields the stages that exist NEED are here. A field nothing reads is a claim
/// nothing checks; a stage that needs another adds it, with the reader that needs it.</para>
/// </remarks>
public sealed record StageDescriptor(
    Stage Stage,
    RoleBucket Bucket,
    string Phrase,
    string Kind,
    CommandStage Commands,
    Stage AdvancesTo,
    string CompletedSentence,
    string ReviseInstruction,
    bool RecordsSha);

/// <summary>The one exhaustive table of stages; adding a stage is adding a row.</summary>
public static class Stages
{
    /// <summary>What every review stage tells the caller to do with a <c>revise</c> verdict.</summary>
    private const string RunThisReviewAgain = "fix the accepted ones, then run this review again";

    /// <summary>
    /// Every stage, in enum order. <see cref="Stage.Done"/> is a row rather than an exception
    /// because <c>status</c> reads a finished session's budget through its bucket, and a round of
    /// it never runs — so its other columns are the values the discards used to answer, kept so
    /// nothing changes by a byte.
    /// </summary>
    public static readonly ImmutableArray<StageDescriptor> All =
    [
        new(Stage.PlanReview, RoleBuckets.PlanCode, "plan review", "plan", CommandStage.Plan,
            AdvancesTo: Stage.CodeReview,
            CompletedSentence: "The plan stage is complete. Implement the plan on the branch, then call review_code.",
            ReviseInstruction: RunThisReviewAgain,
            RecordsSha: false),
        new(Stage.CodeReview, RoleBuckets.ResultCode, "code review", "code", CommandStage.Code,
            AdvancesTo: Stage.Done,
            CompletedSentence: "The code stage is complete. This session is done.",
            ReviseInstruction: RunThisReviewAgain,
            RecordsSha: true),
        new(Stage.Done, RoleBuckets.PlanCode, "done", "code", CommandStage.Any,
            AdvancesTo: Stage.Done,
            CompletedSentence: string.Empty,
            ReviseInstruction: RunThisReviewAgain,
            RecordsSha: false),
        new(Stage.DocumentReview, RoleBuckets.ResultDocument, "document review", "document", CommandStage.Any,
            AdvancesTo: Stage.Done,
            CompletedSentence: "The document stage is complete. This review is done.",
            ReviseInstruction: RunThisReviewAgain,
            RecordsSha: false),
    ];

    private static readonly FrozenDictionary<Stage, StageDescriptor> ByStage =
        All.ToFrozenDictionary(descriptor => descriptor.Stage);

    /// <summary>This stage's row. A stage with no row is a defect, never a default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The stage has no row in <see cref="All"/>.</exception>
    public static StageDescriptor Of(Stage stage) =>
        ByStage.TryGetValue(stage, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(
                nameof(stage), stage, "a stage with no descriptor cannot choose a roster or a sentence — map it here");

    /// <summary>
    /// The phrase for a stage as it was WRITTEN DOWN — a round record's, a session file's — and
    /// the text itself when this build has no phrase for it.
    /// </summary>
    /// <remarks>
    /// Never "done": a round written by a newer build under a stage this one has not heard of is
    /// not a finished one, and a person reading "FeatureReview" can at least look it up.
    /// </remarks>
    public static string PhraseOf(string persisted) =>
        Enum.TryParse<Stage>(persisted, ignoreCase: false, out var stage) && ByStage.TryGetValue(stage, out var descriptor)
            ? descriptor.Phrase
            : persisted;
}
