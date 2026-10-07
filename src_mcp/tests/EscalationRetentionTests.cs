using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The lifecycle of <c>escalations/*.json</c> (<c>todo/PLAN_question_consultant.md</c> A4, D11, S3 acceptance 4):
/// answered and expired pairs, orphans and temp files go seven days after they ended; a question a live
/// session still holds is kept.
/// </summary>
public sealed class EscalationRetentionTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Past = EscalationRetention.Retention + TimeSpan.FromHours(1);
    private static readonly TimeSpan Recent = EscalationRetention.Retention - TimeSpan.FromHours(1);

    private readonly string _data = Directory.CreateTempSubdirectory("coai-esc-ret-").FullName;
    private readonly Escalations _escalations;
    private readonly HashSet<string> _held = [];

    public EscalationRetentionTests() => _escalations = new Escalations(_data);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private EscalationRetention Retention() => new(_escalations, () => new HeldQuestions(_held, Complete: true));

    private EscalationQuestion Asked(string id, TimeSpan ago) => new(
        id, "s-1", "D:/repo", "main", "Ship?", "Ship?", "en", string.Empty, [], (Now - ago).ToString("O"));

    private void Question(string id, TimeSpan askedAgo) => _escalations.Notify(Asked(id, askedAgo));

    private void Answer(string id, TimeSpan answeredAgo)
    {
        Directory.CreateDirectory(_escalations.Directory);
        File.WriteAllText(_escalations.AnswerPath(id), JsonSerializer.Serialize(new { id, answer = "no", answeredUtc = (Now - answeredAgo).ToString("O") }));
    }

    private void Expired(string id, TimeSpan expiredAgo)
    {
        Question(id, expiredAgo + TimeSpan.FromMinutes(15));
        _escalations.Expire(id, Now - expiredAgo).Should().BeTrue();
    }

    private bool Exists(string path) => File.Exists(path);

    [Fact]
    public void AnAnsweredPair_GoesSevenDaysAfterTheAnswer_BothFiles()
    {
        Question("old", Past + TimeSpan.FromHours(1));
        Answer("old", Past);
        Question("young", Recent + TimeSpan.FromHours(1));
        Answer("young", Recent);

        Retention().Sweep(Now).Should().Be(2, "the old question and its answer");

        Exists(_escalations.QuestionPath("old")).Should().BeFalse();
        Exists(_escalations.AnswerPath("old")).Should().BeFalse();
        Exists(_escalations.QuestionPath("young")).Should().BeTrue();
        Exists(_escalations.AnswerPath("young")).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAnsweredPair_CountsTwo_WhicheverFileTheDirectoryListsFirst(bool questionFirst)
    {
        // Windows lists `old.answer.json` before `old.json`; Linux lists in no promised order. With the question
        // first, the pair went at once and the answer — already in the listing — was judged again as an orphan of a
        // file that no longer existed: counted three (CI on ubuntu, PR #646).
        Question("old", Past + TimeSpan.FromHours(1));
        Answer("old", Past);
        string[] pair = [_escalations.QuestionPath("old"), _escalations.AnswerPath("old")];

        Retention().Sweep(questionFirst ? pair : [.. pair.Reverse()], Now).Should().Be(2);

        Exists(_escalations.QuestionPath("old")).Should().BeFalse();
        Exists(_escalations.AnswerPath("old")).Should().BeFalse();
    }

    [Fact]
    public void TheClockIsTheAnswer_NotTheQuestion()
    {
        // Asked long ago, answered yesterday: the pair is recent — the person's words are a day old.
        Question("slow", Past + TimeSpan.FromDays(10));
        Answer("slow", TimeSpan.FromDays(1));

        Retention().Sweep(Now).Should().Be(0);
    }

    [Fact]
    public void AnExpiredQuestion_GoesSevenDaysAfterItExpired()
    {
        Expired("old", Past);
        Expired("young", Recent);

        Retention().Sweep(Now).Should().Be(1);

        Exists(_escalations.QuestionPath("old")).Should().BeFalse();
        Exists(_escalations.QuestionPath("young")).Should().BeTrue("kept for the log until its seven days are up");
    }

    [Fact]
    public void AnOrphanAnswer_AndAStrayTempFile_GoAfterSevenDays()
    {
        Answer("nobody", Past);
        Answer("recent-nobody", Recent);
        Directory.CreateDirectory(_escalations.Directory);
        var tmp = Path.Combine(_escalations.Directory, "abc.json.tmp");
        File.WriteAllText(tmp, "{");
        File.SetLastWriteTimeUtc(tmp, Now - Past);
        var youngTmp = Path.Combine(_escalations.Directory, "def.json.tmp");
        File.WriteAllText(youngTmp, "{");
        File.SetLastWriteTimeUtc(youngTmp, Now - Recent);

        Retention().Sweep(Now).Should().Be(2);

        Exists(_escalations.AnswerPath("nobody")).Should().BeFalse();
        Exists(_escalations.AnswerPath("recent-nobody")).Should().BeTrue();
        Exists(tmp).Should().BeFalse();
        Exists(youngTmp).Should().BeTrue();
    }

    [Fact]
    public void AnOpenQuestionNobodyHolds_GoesAfterSevenDays_AndAHeldOneIsKept()
    {
        Question("forgotten", Past);
        Question("held", Past);
        _held.Add("held");

        Retention().Sweep(Now).Should().Be(1);

        Exists(_escalations.QuestionPath("forgotten")).Should().BeFalse("a call_human notice nobody ever answered is litter after a week");
        Exists(_escalations.QuestionPath("held")).Should().BeTrue("a live session's hold is bound to this id; deleting it would leave the hold unanswerable");
    }

    [Fact]
    public void AnOpenQuestionYoungerThanSevenDays_IsKept()
    {
        Question("fresh", Recent);

        Retention().Sweep(Now).Should().Be(0);
    }

    [Fact]
    public void ATornQuestionFile_IsLitterAfterSevenDays_ByItsWriteTime()
    {
        Directory.CreateDirectory(_escalations.Directory);
        var torn = Path.Combine(_escalations.Directory, "torn.json");
        File.WriteAllText(torn, "{ half");
        File.SetLastWriteTimeUtc(torn, Now - Past);

        Retention().Sweep(Now).Should().Be(1);

        Exists(torn).Should().BeFalse();
    }

    /// <summary>
    /// S4b item 7: a hold is bound to its question by IDENTITY, so a held id keeps its files whatever they say — an
    /// answered pair (the hold reads the answer), an answer whose question is gone, a question file that will not
    /// parse. The class remark already promised "whatever its age or status"; the answered and orphan paths did not ask.
    /// </summary>
    [Fact]
    public void AHeldQuestion_IsKept_WhateverItsStatusOrAge_AnsweredOrphanedOrTorn()
    {
        Question("answered", Past + TimeSpan.FromHours(1));
        Answer("answered", Past);
        Answer("orphan", Past);
        Directory.CreateDirectory(_escalations.Directory);
        var torn = _escalations.QuestionPath("torn");
        File.WriteAllText(torn, "{ half");
        File.SetLastWriteTimeUtc(torn, Now - Past);
        _held.UnionWith(["answered", "orphan", "torn"]);

        Retention().Sweep(Now).Should().Be(0, "every one of them is held by a live session");

        Exists(_escalations.QuestionPath("answered")).Should().BeTrue();
        Exists(_escalations.AnswerPath("answered")).Should().BeTrue("the hold is answered by THIS file");
        Exists(_escalations.AnswerPath("orphan")).Should().BeTrue();
        Exists(torn).Should().BeTrue();
    }

    /// <summary>
    /// The idle-CPU defect of 2026-10-06 (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, D1): what a session
    /// holds is answered by reading EVERY session file, and the sweep asked it once per escalation file on every
    /// one-minute beat, before judging whether the file was even due — 54 files × 435 sessions of JSON a minute, per
    /// server. Nothing young is ever deleted, so a beat with nothing due must not ask at all.
    /// </summary>
    [Fact]
    public void AnIdleBeat_WithNothingDue_NeverAsksWhatTheSessionsHold()
    {
        for (var i = 0; i < 20; i++)
        {
            Question($"young{i}", Recent);
            Answer($"young{i}", Recent - TimeSpan.FromHours(1));
        }

        var asked = 0;
        var retention = new EscalationRetention(_escalations, () => { asked++; return HeldQuestions.None; });
        for (var beat = 0; beat < 10; beat++)
        {
            retention.Sweep(Now).Should().Be(0);
        }

        asked.Should().Be(0, "nothing is due, so what the sessions hold cannot change what this beat deletes");
    }

    /// <summary>The other half of D1: when files ARE due, the sessions are read once for the sweep, never once per file.</summary>
    [Fact]
    public void ABeatWithSeveralDueFiles_AsksWhatTheSessionsHold_OncePerSweep()
    {
        for (var i = 0; i < 5; i++)
        {
            Question($"old{i}", Past + TimeSpan.FromHours(1));
            Answer($"old{i}", Past);
        }

        Question("held", Past);
        _held.Add("held");
        var asked = 0;
        var retention = new EscalationRetention(_escalations, () => { asked++; return new HeldQuestions(_held, Complete: true); });

        retention.Sweep(Now).Should().Be(10, "five answered pairs went; the held question stayed");
        asked.Should().Be(1);
        Exists(_escalations.QuestionPath("held")).Should().BeTrue();
    }

    /// <summary>
    /// A session that could not be read (torn, or busy under another writer's turn) may hold any id, so a set read
    /// without it deletes nothing that is due — it waits for a beat that can read every session (own review, 2026-10-06:
    /// the per-id read it replaces treated an unreadable session as holding nothing, and could take a held card).
    /// </summary>
    [Fact]
    public void ASetReadWithoutEverySession_KeepsEveryDueFile()
    {
        Question("old", Past + TimeSpan.FromHours(1));
        Answer("old", Past);
        Question("forgotten", Past);

        new EscalationRetention(_escalations, () => new HeldQuestions(new HashSet<string>(), Complete: false))
            .Sweep(Now).Should().Be(0);

        Exists(_escalations.QuestionPath("old")).Should().BeTrue();
        Exists(_escalations.QuestionPath("forgotten")).Should().BeTrue();
    }

    [Fact]
    public void NoDirectoryYet_IsNothingToSweep() =>
        new EscalationRetention(new Escalations(Path.Combine(_data, "never")), () => HeldQuestions.None).Sweep(Now).Should().Be(0);
}
