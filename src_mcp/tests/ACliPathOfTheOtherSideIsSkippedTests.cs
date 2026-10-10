using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reviewer row's CLI path spelled for the OTHER operating system is skipped — the runtime's CLI is looked up on PATH —
/// and said once at Information, never probed as a file and never refused (todo/PLAN_paths_per_side.md E1.2).
/// </summary>
/// <remarks>
/// VS Code shares its user settings between a WSL window and a Windows window, and each side runs its own coai-mcp. A
/// settings file written by an OLDER extension still carries the other side's <c>C:\…\codex.cmd</c> in <c>COAI_VENDORS</c>;
/// the probe of that file answered <c>CliFound=false</c> and the card said "cannot review". The extension now writes
/// this side's value itself; this is the server's half, answered from the same vector file.
/// </remarks>
public sealed class ACliPathOfTheOtherSideIsSkippedTests
{
    /// <summary>A CLI path spelled for the OS this test does NOT run on — the other side's, wherever the suite runs.</summary>
    private static readonly string OtherSides = OperatingSystem.IsWindows() ? "/usr/local/bin/codex" : @"C:\Users\jinx\AppData\Roaming\npm\codex.cmd";

    private static readonly string ThisSides = OperatingSystem.IsWindows() ? @"C:\tools\codex.exe" : "/opt/codex/bin/codex";

    private static ProviderSettings Read(string executablePath)
    {
        var vendors = System.Text.Json.JsonSerializer.Serialize(new[] { new Dictionary<string, string> { ["id"] = "codex", ["runtime"] = "codex", ["executablePath"] = executablePath } });
        var env = new Dictionary<string, string> { ["COAI_VENDORS"] = vendors };

        return PanelSettings.FromEnvironment(name => env.GetValueOrDefault(name)).Providers.Should().ContainSingle().Subject;
    }

    [Fact]
    public void ExecutablePaths_AnswerTheSharedExecutableVectors_AsTheExtensionDoes()
    {
        var vectors = SharedVectors.Rows("executable");

        vectors.Should().HaveCountGreaterThan(5, "the file is read, not an empty array");
        vectors.Should().Contain(v => v.Flag("existsHere") && !v.Flag("otherSide"), "an existing root-relative file is pinned in the file");
        foreach (var vector in vectors)
        {
            var windows = vector.Flag("windows");
            var presence = vector.Flag("existsHere") ? RootPresence.Present : vector.OptionalFlag("unknownHere") ? RootPresence.Unknown : RootPresence.Absent;
            var qualified = QuestionRoots.Qualified(vector.Text("path"), windows, vector.Text("systemDrive"));
            var asked = new List<string>();
            var answer = ExecutablePaths.Here(vector.Text("path"), Places(windows, vector.Text("systemDrive")), full => { asked.Add(full); return presence; });
            var said = $"{(windows ? "windows" : "posix")}: '{vector.Text("path")}' — {vector.Text("why")}";
            answer.Path.Should().Be(vector.Text("here"), said);
            answer.OtherSide.Should().Be(vector.Flag("otherSide") ? vector.Text("path").Trim() : string.Empty, said);
            asked.Where(full => full != qualified).Should().BeEmpty($"{said}: the disk is asked about the qualified path alone");
        }
    }

    private static SystemPlaces Places(bool windows, string systemDrive) => new(string.Empty, []) { Windows = windows, SystemDrive = systemDrive };

    [Fact]
    public void TheConsultantsCase_AnExistingRootRelativeCliOnWindows_RunsQualifiedWithTheSystemDrive_NeverAPathInstall()
    {
        // E1's cadence consultation: `/Program Files/nodejs/node.exe --version` launches from C:\ — skipping it lexically ran
        // a different installation. The disk is injected, so this is the same test on any machine.
        RootPresence On(string full, RootPresence there) => full == @"Q:\Program Files\nodejs\node.exe" ? there : RootPresence.Absent;

        ExecutablePaths.Here("/Program Files/nodejs/node.exe", Places(true, "Q:"), full => On(full, RootPresence.Present))
            .Should().Be(new ThisSidePath(@"Q:\Program Files\nodejs\node.exe", string.Empty));
        ExecutablePaths.Here("/Program Files/nodejs/node.exe", Places(true, "Q:"), full => On(full, RootPresence.Unknown))
            .Should().Be(new ThisSidePath(@"Q:\Program Files\nodejs\node.exe", string.Empty), "never skipped on a guess");
        ExecutablePaths.Here("/Program Files/nodejs/node.exe", Places(true, "Q:"), full => On(full, RootPresence.Absent))
            .Should().Be(new ThisSidePath(string.Empty, "/Program Files/nodejs/node.exe"));
    }

    [Fact]
    public void TheFileProbe_TellsAFileADirectoryAndNothingApart_OnTheRealDisk()
    {
        using var dir = TempDir.For("coai-clipath-");
        var file = Path.Combine(dir.Path, "codex.exe");
        File.WriteAllText(file, "x");

        ExecutablePaths.FilePresenceOf(file).Should().Be(RootPresence.Present);
        ExecutablePaths.FilePresenceOf(dir.Path).Should().Be(RootPresence.Absent, "a directory where the CLI should be is no CLI");
        ExecutablePaths.FilePresenceOf(Path.Combine(dir.Path, "gone.exe")).Should().Be(RootPresence.Absent);
        QuestionRoots.PresenceOf(dir.Path).Should().Be(RootPresence.Present, "the roots' folder probe is unchanged");
    }

    [Fact]
    public void AVendorRowWhoseCliPathIsTheOtherSides_IsProbedByItsPathName_AndTheSkippedValueIsKeptForTheLog()
    {
        var provider = Read(OtherSides);

        provider.ExecutablePath.Should().BeEmpty("an empty path is the PATH lookup of the runtime's own name — never the other side's file");
        provider.OtherSideExecutable.Should().Be(OtherSides, "the skipped value is named, so the operator's log can say whose it is");
    }

    [Fact]
    public void AVendorRowWhoseCliPathIsThisSides_KeepsIt()
    {
        var provider = Read($"  {ThisSides}  ");

        provider.ExecutablePath.Should().Be(ThisSides);
        provider.OtherSideExecutable.Should().BeEmpty();
    }

    [Fact]
    public void ASkippedCliPath_IsLoggedAtInformationOnce_AndNeverReachesThePageAsANotice()
    {
        var written = new List<ServerNotice>();
        var said = new List<(Serilog.Events.LogEventLevel Level, string Text)>();
        var log = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new Sink(said)).CreateLogger();
        var settings = new PanelSettings { Providers = [new ProviderSettings("codex") { OtherSideExecutable = OtherSides }, new ProviderSettings("claude")] };

        StartupNotices.Record(settings, [], new Noticing(notice => { written.Add(notice); return true; }, log), log);

        written.Should().BeEmpty("the other side's CLI is not a setting this build stood down from");
        said.Should().ContainSingle(line => line.Text.Contains(OtherSides, StringComparison.Ordinal))
            .Which.Should().Match<(Serilog.Events.LogEventLevel Level, string Text)>(line =>
                line.Level == Serilog.Events.LogEventLevel.Information && line.Text.Contains("codex's CLI path", StringComparison.Ordinal)
                && line.Text.Contains("looks the CLI up on PATH", StringComparison.Ordinal));
    }

    private sealed class Sink(List<(Serilog.Events.LogEventLevel Level, string Text)> said) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            lock (said)
            {
                said.Add((logEvent.Level, logEvent.RenderMessage()));
            }
        }
    }
}
