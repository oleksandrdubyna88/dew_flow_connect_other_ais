using System.Diagnostics;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Security;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The security lane's signal words are editable, and a card has words of its own — a word, a phrase, a piece of code, or
/// a <c>/regex/</c> (research/PLAN_one_model_catalog.md, epic 2, story 4).
/// </summary>
/// <remarks>
/// <para>A signal's words, when the setting gives them, REPLACE its shipped words; its shipped pattern (the SQL
/// statement shapes) stays, because a person editing the words never saw it. A card's own words are one more signal,
/// <c>own:&lt;prompt id&gt;</c>, that only that card is triggered by.</para>
/// <para>Patterns run on <see cref="System.Text.RegularExpressions.RegexOptions.NonBacktracking"/> with a match timeout:
/// what NonBacktracking cannot compile (lookaround, a backreference) is refused BY NAME, and a pattern that runs out of
/// time leaves its file's detection incomplete rather than quietly unmatched.</para>
/// </remarks>
public sealed class SecurityWordsAreEditableTests
{
    private static FileDiff File(string path, string text) => new(path, text, false);

    private static IReadOnlyList<string> SignalsOf(FileDiff file, SignalTable table) =>
        SecuritySignals.Classify([file], table)[0].Signals;

    [Fact]
    public void TheShippedTable_DetectsWhatTodaysDetectorDoes()
    {
        var file = File("src/Db.cs", "var x = new DbContext();");

        SecuritySignals.Classify([file])[0].Signals.Should().Equal(SignalsOf(file, SignalTable.Shipped));
        SignalsOf(file, SignalTable.Shipped).Should().Contain("sql");
    }

    [Fact]
    public void ASignalsWords_ReplaceItsShippedWords_ButKeepItsShippedPattern()
    {
        var table = SignalTable.Build(new Dictionary<string, IReadOnlyList<string>> { ["sql"] = ["mydbhelper"] }, []);

        SignalsOf(File("src/A.cs", "MyDbHelper.Run();"), table).Should().Contain("sql", "the person's word");
        SignalsOf(File("src/B.cs", "new DbContext();"), table).Should().NotContain("sql", "the shipped word was replaced");
        SignalsOf(File("src/C.cs", "\"select id from users\""), table).Should().Contain("sql", "the statement shapes stay");
        table.Refused.Should().BeEmpty();
    }

    [Fact]
    public void ACardsOwnWords_TriggerThatCardAlone()
    {
        var table = SignalTable.Build(new Dictionary<string, IReadOnlyList<string>>(), [new("redteam-billing", ["acme.charge(", "/\\bacme\\d{3}\\b/"])]);
        var card = new SecurityPrompt("redteam-billing", [], []) { Words = ["acme.charge(", "/\\bacme\\d{3}\\b/"] };
        var other = new SecurityPrompt("redteam-other", [], []);

        SecuritySignals.Triggered(card, SecuritySignals.Classify([File("src/Pay.cs", "Acme.Charge(order);")], table)).Should().BeTrue("a word");
        SecuritySignals.Triggered(card, SecuritySignals.Classify([File("src/Pay.cs", "id = acme123;")], table)).Should().BeTrue("a pattern");
        SecuritySignals.Triggered(card, SecuritySignals.Classify([File("src/Pay.cs", "nothing here")], table))
            .Should().BeFalse("a card with words of its own runs when they match — not on every change");
        SecuritySignals.Classify([File("src/Pay.cs", "Acme.Charge(order);")], table)[0].Signals.Should().Contain(SecurityPrompt.OwnSignalOf("redteam-billing"));
        other.Words.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/(?=secret)/", "lookaround")]
    [InlineData("/(a)\\1/", "backreference")]
    [InlineData("/[unclosed/", "pattern")]
    public void APatternTheEngineCannotRun_IsRefusedByName(string pattern, string why)
    {
        var table = SignalTable.Build(new Dictionary<string, IReadOnlyList<string>> { ["secrets"] = [pattern, "password"] }, []);

        table.Refused.Should().ContainSingle().Which.Should().Match<PatternRefusal>(r => r.Pattern == pattern && r.Signal == "secrets");
        table.Refused[0].Why.Should().Contain(why);
        SignalsOf(File("src/A.cs", "password = x"), table).Should().Contain("secrets", "the rest of the list still works");
    }

    [Fact]
    public void ALongPattern_AndPatternsPastTheCap_AreRefusedByName()
    {
        var tooLong = "/" + new string('a', SignalTable.MaxPatternLength + 1) + "/";
        var many = Enumerable.Range(0, SignalTable.MaxPatterns + 1).Select(i => $"/p{i}x/").ToList();
        var table = SignalTable.Build(new Dictionary<string, IReadOnlyList<string>> { ["secrets"] = [tooLong], ["crypto"] = many }, []);

        table.Refused.Should().Contain(r => r.Pattern == tooLong && r.Why.Contains($"{SignalTable.MaxPatternLength}"));
        table.Refused.Should().Contain(r => r.Why.Contains($"{SignalTable.MaxPatterns} patterns"));
    }

    [Fact]
    public void ACatastrophicPattern_OverAMegabyte_FinishesAndTheRoundGoesOn()
    {
        var table = SignalTable.Build(new Dictionary<string, IReadOnlyList<string>> { ["secrets"] = ["/(a+)+$/", "/(x|xx)+y/"] }, []);
        var huge = File("src/Big.cs", new string('a', SecuritySignals.MaxFileCharacters - 1) + "!");
        var watch = Stopwatch.StartNew();

        var classified = SecuritySignals.Classify([huge], table);

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "NonBacktracking is linear in the input; a backtracking engine would not return");
        classified.Should().ContainSingle();
    }
}
