using CoaiMcp.Acp;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The feature gate's round 2 on the spent-quota plan (T1 and T2): a diagnosis found only on the agent's STDERR keeps the sentence it was found in, on every line a person reads, and a reset clause quoted
/// from such a line is the vendor's words and nothing the runtime added. Every case is one real round — a fake agent, the real shim, the real runtime, executor, scheduler, ledger and round audit
/// (<see cref="AKimiRound"/>) — and asserts the round's summary, ledger line and log line.
/// </summary>
public sealed class TheStderrOnlyEvidenceReachesThePersonTests : IDisposable
{
    private const string NamesNoRow = "Internal error";

    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-stderr-evidence-");

    public void Dispose() => _dir.Dispose();

    private static void EveryLineQuotesOnce(TheRound round, string sentence)
    {
        foreach (var (where, line) in round.Lines)
        {
            line.Should().Contain(sentence, $"the {where} must carry the sentence the vendor wrote on stderr");
            line.Split(sentence).Length.Should().Be(2, $"the {where} must quote it once, not twice");
        }
    }

    // ---------- T1: the note carries the matched line ----------

    [Fact]
    public async Task AProviderError_FoundOnlyOnStderr_KeepsTheProvidersSentenceInTheSummaryTheLedgerAndTheLog()
    {
        const string sentence = "Authentication required: 503 Service Unavailable";

        var round = await new AKimiRound(_dir).RunAsync(NamesNoRow, sentence + "\n");

        round.Outcome.Should().BeOfType<ReviewerOutcome.NonZeroExit>();
        EveryLineQuotesOnce(round, sentence);
    }

    [Fact]
    public async Task ANotSignedInRefusal_FoundOnlyOnStderr_KeepsTheProvidersSentenceInTheSummaryTheLedgerAndTheLog()
    {
        const string sentence = "Authentication required, token expired at 09:00 UTC";

        var round = await new AKimiRound(_dir).RunAsync(NamesNoRow, "kimi: " + sentence + "\n");

        EveryLineQuotesOnce(round, sentence);
        round.Summary.Should().Contain("kimi login", "the cure still leads: the sign-in cure is the row's own sentence");
    }

    [Fact]
    public async Task TheShimsNote_CarriesTheMatchedLine_BeforeTheCure_SoWithoutCureStillReadsTheNoteBack()
    {
        const string sentence = "Authentication required, token expired at 09:00 UTC";
        var said = await AKimiRound.TheShimSaysAsync(NamesNoRow, "kimi: " + sentence + "\n");
        var note = ShimNotes.Message(said.StdErr.Split('\n').Single(line => ShimNotes.Ours(line)));

        note.Should().Contain(sentence);
        var withoutCure = AcpDiagnosis.WithoutCure(note, KimiAcpProfile.Failures);
        withoutCure.Should().NotBe(note, "the cure is still the LAST thing in the note");
        withoutCure.Should().EndWith(sentence + ")").And.NotContain("kimi login");
    }

    [Fact]
    public async Task AReasonThatAlreadyNamesTheRow_IsNotQuotedASecondTimeFromStderr()
    {
        // The reason held the phrase, so there is no matched line (Line is empty): the note is what it always was.
        var said = await AKimiRound.TheShimSaysAsync("Authentication required: 503 Service Unavailable");
        var note = ShimNotes.Message(said.StdErr.Split('\n').Single(line => ShimNotes.Ours(line)));

        note.Should().NotContain("the agent's stderr");
    }

    // ---------- T2: the clause is the vendor's words, not the runtime's punctuation ----------

    [Fact]
    public async Task AResetClause_FromAStderrOnlyLineEndingInAPeriod_HasNoClosingParenthesisOfOurs()
    {
        const string clause = "Your quota will reset at 14:00 UTC.";

        var round = await new AKimiRound(_dir).RunAsync(NamesNoRow, "provider.api_error: 429 You've reached your 5-hour usage limit. " + clause + "\n");

        foreach (var (where, line) in round.Lines)
        {
            line.Should().Contain($"\"{clause}\"", $"the {where} quotes the clause whole, closed by the vendor's own period");
            line.Should().NotContain("UTC.)\"", $"the {where}: the runtime's closing parenthesis is not part of the quoted clause");
        }
    }

    [Theory]
    [InlineData("(Kimi said: ", ")")]
    [InlineData("(the agent's stderr: ", ") — the cure")]
    public void AClause_StopsAtAClosingParenthesisItDidNotOpen_WhateverWrapperClosesIt(string wrapper, string tail)
    {
        var text = $"{KimiAcpProfile.QuotaSpent}: {NamesNoRow} {wrapper}You've reached your 5-hour usage limit. Your quota will reset at 14:00 UTC.{tail}";

        KimiQuotaWords.ClauseOf(text).Should().Be("Your quota will reset at 14:00 UTC.");
        KimiQuotaWords.Said(text).Should().Contain("\"Your quota will reset at 14:00 UTC.\"");
    }

    [Fact]
    public void AClauseWithItsOwnParenthesis_KeepsIt()
    {
        var text = $"{KimiAcpProfile.QuotaSpent}: {NamesNoRow} (Kimi said: You've reached your 5-hour usage limit. Your quota will reset in 2 hours (14:00 UTC).)";

        KimiQuotaWords.ClauseOf(text).Should().Be("Your quota will reset in 2 hours (14:00 UTC).");
    }
}
