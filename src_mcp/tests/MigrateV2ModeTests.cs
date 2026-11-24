using System.Text.Json;
using CoaiMcp.Core.Rounds;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--migrate-v2</c> as a one-shot mode of the REAL binary (E1.S4, scenario 19, and <c>.agents/PROJECT.md</c>): stdout is its interface — one JSON object —
/// stderr carries logs only, a wrong argument is 65, and the mode never exits 64.
/// </summary>
public sealed class MigrateV2ModeTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-migrate-v2-");

    private string Data => _dir.Path;

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task S19_ABogusArgument_AnswersExit65_NeverTheDetectionCode64_WithOneJsonObjectOnStdout()
    {
        MigrateV2Data.Plant("f3", "plan-pending", Data);
        var before = MigrateV2Data.Tree(Data, withFolders: true, withBookkeeping: true);

        var (code, output, err) = await MigrateV2Data.Binary(Data, "--migrate-v2", "--bogus");

        code.Should().Be(65, "the binary KNOWS this mode: 64 means never heard of it, and is how a caller detects an old binary");
        var report = MigrateV2Data.Report(output);
        report.GetProperty("status").GetString().Should().Be("failed");
        report.GetProperty("error").GetString().Should().Contain("--apply");
        output.Trim().Split('\n').Should().NotBeEmpty();
        err.Should().NotContain("{", "stderr is for the sentence, never for the report");
        err.Should().Contain("--migrate-v2");
        MigrateV2Data.Tree(Data, withFolders: true, withBookkeeping: true).Should().Equal(before, "a refused request changes nothing");
    }

    [Theory]
    [InlineData("--apply", "--apply")]
    [InlineData("--apply", "--bogus")]
    [InlineData("--dry-run")]
    public async Task S19_AnyOtherArgument_IsRefusedWith65(params string[] extra)
    {
        var (code, output, _) = await MigrateV2Data.Binary(Data, ["--migrate-v2", .. extra]);

        code.Should().Be(65);
        MigrateV2Data.Report(output).GetProperty("status").GetString().Should().Be("failed");
    }

    [Fact]
    public async Task S19_ADryRunOfTheRealBinary_PrintsOneObject_CreatesNothing_AndExitsZero()
    {
        MigrateV2Data.Plant("f3", "plan-pending", Data);
        var before = MigrateV2Data.Tree(Data, withFolders: true, withBookkeeping: true);

        var (code, output, err) = await MigrateV2Data.Binary(Data, "--migrate-v2");

        code.Should().Be(0, err);
        var report = MigrateV2Data.Report(output);
        report.GetProperty("status").GetString().Should().Be("plan");
        report.GetProperty("dataDir").GetString().Should().Be(Data);
        report.GetProperty("counts").GetProperty("sessions").GetInt32().Should().Be(1);
        report.GetProperty("counts").GetProperty("cards").GetInt32().Should().Be(1);
        MigrateV2Data.Tree(Data, withFolders: true, withBookkeeping: true).Should().Equal(before, "a dry run creates nothing — not migrations/, not a lease, not a log file");
        Directory.Exists(Path.Combine(Data, "logs")).Should().BeFalse();
    }

    [Fact]
    public async Task S19_AnApplyOfTheRealBinary_MigratesTheFolder_AndPrintsWhatItDid()
    {
        MigrateV2Data.Plant("f1", "feature-pending", Data);

        var (code, output, err) = await MigrateV2Data.Binary(Data, "--migrate-v2", "--apply");

        code.Should().Be(0, err);
        var report = MigrateV2Data.Report(output);
        report.GetProperty("status").GetString().Should().Be("done");
        report.GetProperty("apply").GetBoolean().Should().BeTrue();
        report.GetProperty("counts").GetProperty("sessions").GetInt32().Should().Be(1);
        report.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("action").GetString()).Should().Contain("convert");
        LegacyHold.Stands(MigrateV2Data.Load(Data, MigrateV2Data.SessionFile(Data, "feature")).State).Should().BeFalse();
        File.Exists(Path.Combine(Data, "migrations", "v2.done")).Should().BeTrue();
        err.Should().NotContain("{\"mode\"", "stderr never carries the report");
    }

    [Fact]
    public void TheModeIsAWriter_ClassifiedByItsOwnFlag_AndTheDefaultsAreHelpedToo()
    {
        Program.Classify(["--migrate-v2"]).Should().Be(Program.Startup.MigrateV2);
        Program.WriterModes.Should().Contain(Program.Startup.MigrateV2, "a named data folder that is missing is refused before it runs, as for every writer");
        Program.HelpText.Should().Contain("--migrate-v2 [--apply]");
    }

    [Fact]
    public void AMissingDataFolder_IsExit74_WithOneObjectOnStdout()
    {
        var (code, output, _) = MigrateV2Data.Mode(Path.Combine(Data, "no-such-folder"), "--apply");

        code.Should().Be(74);
        JsonDocument.Parse(output).RootElement.GetProperty("status").GetString().Should().Be("failed");
        Directory.Exists(Path.Combine(Data, "no-such-folder")).Should().BeFalse("the mode never makes the folder");
    }
}
