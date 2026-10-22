using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// §13.3 of the S2 spec (security judge, 2026-10-22): a withdrawal that HOLDS the gate sends the hold's notice only when the hold was saved. The notice
/// is bound to the session by identity (<c>HoldQuestions</c>); a session the disk refused never recorded it, so the card would be an orphan — a question
/// whose answer releases nothing. The refusal is the one <see cref="RoundEngine.Recorded"/> reports, so the test drives that seam and the decision it feeds.
/// </summary>
public sealed class AHoldThatCouldNotBeSavedSendsNoNoticeTests
{
    private static readonly WithdrawalStreak Twice = new(2, 3);

    private static bool SavedTo(Action save) =>
        RoundEngine.Recorded(save, new Serilog.LoggerConfiguration().CreateLogger(), 1);

    [Fact]
    public void AHoldThatLanded_SendsItsNotice() =>
        Twice.NoticeFor(held: true, landed: SavedTo(() => { })).Should().Be(WithdrawalNotice.Hold);

    [Fact]
    public void AHoldTheDiskRefused_SendsNoNotice_AndIsToldApart()
    {
        var landed = SavedTo(() => throw new SessionStoreException("the session could not be written", new IOException("disk full")));

        Twice.NoticeFor(held: true, landed).Should().Be(WithdrawalNotice.HoldLost,
            "an orphan card bound to no HoldQuestions would be asked and answer nothing");
    }

    [Theory]
    [InlineData(1, 3, false, WithdrawalNotice.None)]
    [InlineData(2, 3, false, WithdrawalNotice.Mention)]
    [InlineData(2, 3, true, WithdrawalNotice.Mention)]
    [InlineData(3, 4, false, WithdrawalNotice.None)]
    public void AFreeWithdrawal_MentionsOnlyTheSecondInARow_WhateverBecameOfItsSave(int count, int cap, bool landed, WithdrawalNotice expected) =>
        new WithdrawalStreak(count, cap).NoticeFor(held: false, landed).Should().Be(expected);
}
