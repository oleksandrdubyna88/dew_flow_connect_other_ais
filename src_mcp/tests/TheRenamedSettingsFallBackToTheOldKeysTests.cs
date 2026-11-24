using System.Diagnostics;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>COAI_CONS_*</c> and <c>COAI_CONSASK_*</c> are the names of what v1 called <c>COAI_CONSULT_*</c> and <c>COAI_QCONSULT_*</c>
/// (v2 E3.S3 t2). The server reads the new name; an unset new key falls back to the old one; when both are set the new one wins.
/// </summary>
[Collection("fakecli-env")] // the server child inherits our env; keep FAKECLI_* quiet around it
public sealed class TheRenamedSettingsFallBackToTheOldKeysTests
{
    private static Func<string, string?> Env(params (string Name, string Value)[] rows) =>
        name => rows.FirstOrDefault(row => row.Name == name) is { Name.Length: > 0 } found ? found.Value : null;

    private static PanelSettings From(params (string Name, string Value)[] rows) => PanelSettings.FromEnvironment(Env(rows));

    [Fact]
    public void TheNewKeys_AreRead()
    {
        var settings = From(("COAI_CONS_TURNS", "3"), ("COAI_CONS_CALLS_PER_SESSION", "4"), ("COAI_CONS_IDLE_MINUTES", "30"), ("COAI_CONS_ENABLED", "false"));

        settings.ConsultTurns.Should().Be(3);
        settings.ConsultCallsPerSession.Should().Be(4);
        settings.ConsultIdle.Should().Be(TimeSpan.FromMinutes(30));
        settings.ConsultEnabled.Should().BeFalse();
    }

    [Fact]
    public void TheOldKeys_AnswerWhileTheNewOnesAreUnset()
    {
        var settings = From(("COAI_CONSULT_TURNS", "2"), ("COAI_CONSULT_CALLS_PER_SESSION", "6"), ("COAI_CONSULT_IDLE_MINUTES", "20"), ("COAI_CONSULT_ENABLED", "0"));

        settings.ConsultTurns.Should().Be(2);
        settings.ConsultCallsPerSession.Should().Be(6);
        settings.ConsultIdle.Should().Be(TimeSpan.FromMinutes(20));
        settings.ConsultEnabled.Should().BeFalse();
    }

    [Fact]
    public void WhenBothAreSet_TheNewKeyWins_InBothDirections()
    {
        var newSaysOff = From(("COAI_CONS_ENABLED", "false"), ("COAI_CONSULT_ENABLED", "true"), ("COAI_CONS_TURNS", "7"), ("COAI_CONSULT_TURNS", "2"));
        var oldSaysOff = From(("COAI_CONS_ENABLED", "true"), ("COAI_CONSULT_ENABLED", "false"), ("COAI_CONS_TURNS", "2"), ("COAI_CONSULT_TURNS", "7"));

        newSaysOff.ConsultEnabled.Should().BeFalse();
        newSaysOff.ConsultTurns.Should().Be(7);
        oldSaysOff.ConsultEnabled.Should().BeTrue("the old key says off and the new key says on: the new key wins");
        oldSaysOff.ConsultTurns.Should().Be(2);
    }

    [Fact]
    public void AnEmptyNewKey_IsUnset_SoTheOldOneStillAnswers()
    {
        From(("COAI_CONS_TURNS", ""), ("COAI_CONSULT_TURNS", "9")).ConsultTurns.Should().Be(9);
    }

    [Fact]
    public void Consask_ReadsItsNewKeys_FallsBackToTheOldOnes_AndTheNewOneWins()
    {
        var places = new SystemPlaces("/home/nobody", []);
        QuestionConsultSettings Read(params (string Name, string Value)[] rows) =>
            QuestionConsultReader.Read(Env(rows), "/data", places, _ => true).Settings;

        Read(("COAI_CONSASK_ROW_MINUTES", "3"), ("COAI_CONSASK_QUESTIONS_PER_SESSION", "4"), ("COAI_CONSASK_ENABLED", "false"))
            .Should().BeEquivalentTo(new { RowBudget = TimeSpan.FromMinutes(3), QuestionsPerSession = 4, Enabled = false }, options => options.ExcludingMissingMembers());

        var old = Read(("COAI_QCONSULT_ROW_MINUTES", "6"), ("COAI_QCONSULT_QUESTIONS_PER_SESSION", "8"), ("COAI_QCONSULT_ENABLED", "false"));
        old.RowBudget.Should().Be(TimeSpan.FromMinutes(6));
        old.QuestionsPerSession.Should().Be(8);
        old.Enabled.Should().BeFalse();

        var both = Read(("COAI_CONSASK_ROW_MINUTES", "2"), ("COAI_QCONSULT_ROW_MINUTES", "9"), ("COAI_CONSASK_ENABLED", "true"), ("COAI_QCONSULT_ENABLED", "false"));
        both.RowBudget.Should().Be(TimeSpan.FromMinutes(2), "both set: the new key wins");
        both.Enabled.Should().BeTrue("both set: the new key wins");
    }

    [Fact]
    public void Consask_ReadsTheRowsUnderTheNewKey_AndTheOldKeyStillCarriesThem()
    {
        const string rows = """[{"id":"r1","vendor":"codex","runtime":"codex","prompt":"question-opinion"}]""";
        var places = new SystemPlaces("/home/nobody", []);

        QuestionConsultReader.Read(Env(("COAI_CONSASK_ROWS", rows)), "/data", places, _ => true).Settings.Rows.Select(r => r.Id).Should().Equal("r1");
        QuestionConsultReader.Read(Env(("COAI_QCONSULT_ROWS", rows)), "/data", places, _ => true).Settings.Rows.Select(r => r.Id).Should().Equal("r1");
    }

    [Theory]
    [InlineData("COAI_CONS_TURNS", "COAI_CONSULT_TURNS")]
    [InlineData("COAI_CONSASK_ROWS", "COAI_QCONSULT_ROWS")]
    [InlineData("COAI_CONSULTANTS", null)]
    [InlineData("COAI_CONSULTANT_HOMES", null)]
    [InlineData("COAI_MAX_ROUNDS", null)]
    public void TheFallbackIsARuleOverTheTwoPrefixes_AndNothingElse(string name, string? old)
    {
        PanelSettings.OldNameOf(name).Should().Be(old);
    }

    /// <summary>
    /// Over the real wire: the new key switches <c>cons</c> off, and when both are set the new one decides. The refusal names the key
    /// to set, so a person who reads it sets the new one.
    /// </summary>
    [Theory]
    [InlineData("COAI_CONS_ENABLED", "false", null, null, true)]
    [InlineData(null, null, "COAI_CONSULT_ENABLED", "false", true)]
    [InlineData("COAI_CONS_ENABLED", "false", "COAI_CONSULT_ENABLED", "true", true)]
    [InlineData("COAI_CONS_ENABLED", "true", "COAI_CONSULT_ENABLED", "false", false)]
    public async Task OverTheWire_TheNewKeyWins_AndTheOldKeyStillCounts(string? newKey, string? newValue, string? oldKey, string? oldValue, bool switchedOff)
    {
        using var data = TempDir.For("coai-cons-keys-");
        var environment = new List<(string Name, string Value)>();
        if (newKey is not null)
        {
            environment.Add((newKey, newValue!));
        }

        if (oldKey is not null)
        {
            environment.Add((oldKey, oldValue!));
        }

        using var server = StdioServer.Start(data.Path, "debug", 1800, [.. environment]);
        try
        {
            await StdioServer.InitializeAsync(server);
            var answer = await StdioServer.Call(server, 2, "cons", """{"repoPath":"/nowhere","problem":"stuck"}""");

            var said = answer.TryGetProperty("error", out var error) ? error.GetString()! : string.Empty;
            (said.Contains("switched off", StringComparison.Ordinal)).Should().Be(switchedOff, said);
        }
        finally
        {
            await StdioServer.StopAsync(server);
        }
    }
}
