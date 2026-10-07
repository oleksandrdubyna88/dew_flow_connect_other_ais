using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The person's flow of todo/PLAN_fast_mode.md, Story D, below the page: a row with its fast mode set is handed to
/// <c>--check-model</c> exactly as the card's ✓ Check hands it, and the CLI the check really launches is told the tier —
/// the fake CLI records the argv it was started with. The page half (the select drawn, saved, sent only to a binary that
/// lists <c>fastMode</c>) is <c>aRowHasAFastModeSwitch.test.ts</c>; the seam leg reads <c>--providers</c>' <c>fast</c>.
/// </summary>
[Collection("fakecli-env")]
public sealed class AFastModeRowIsCheckedTests : IDisposable
{
    private static readonly string[] Steering = ["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_RECORD_DIR"];

    private readonly string _data = Directory.CreateTempSubdirectory("coai-fastcheck-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("coai-fastcheck-temp-").FullName;
    private readonly string _record = Directory.CreateTempSubdirectory("coai-fastcheck-argv-").FullName;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public AFastModeRowIsCheckedTests()
    {
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
        // The codex stand-in: a thread id on stdout, the answer in its -o file.
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"type":"thread.started","thread_id":"0198f2c1-fast"}""" + "\n");
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", "marker: {{cwd-file:CHECK.md}}\ncanary: CANNOT");
    }

    public void Dispose()
    {
        foreach (var name in Steering)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_data, _temp, _record])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not the behaviour under test.
            }
        }
    }

    private async Task<IReadOnlyList<string>> CheckedArgv(string fast)
    {
        var row = "{\"row\":{\"id\":\"codex-2\",\"runtime\":\"codex\",\"executablePath\":" + JsonSerializer.Serialize(FakeCliExe) + fast + "}}";
        var (code, _, stderr) = await ConsultantCheckMode.AnswerModelAsync(
            new PanelSettings { Providers = [], DataDir = _data, ReviewerTimeout = TimeSpan.FromSeconds(60), ConsultEnabled = true },
            row, new ProcessLauncher(), _temp, _ => { }, Noticing.None, TestContext.Current.CancellationToken, VaultKeys.None("t"));
        code.Should().Be(0, stderr);
        var launched = Directory.EnumerateFiles(_record, "*.argv").Single();

        // The recorder writes the argv NUL-joined (a field may be multiline); the last field is the prompt on stdin.
        return File.ReadAllText(launched).Split('\0');
    }

    private static IEnumerable<string> Tiers(IReadOnlyList<string> argv) =>
        argv.Zip(argv.Skip(1)).Where(pair => pair.First == "-c" && pair.Second.StartsWith("service_tier=", StringComparison.Ordinal)).Select(pair => pair.Second);

    [Fact]
    public async Task ARowSetToFast_IsCheckedWithTheFastTier_ThroughTheRealLaunch()
    {
        Tiers(await CheckedArgv(",\"fast\":\"on\"")).Should().Equal("service_tier=fast");
    }

    [Fact]
    public async Task ARowThatNeverSetIt_IsCheckedOnTheStandardTier()
    {
        Tiers(await CheckedArgv(string.Empty)).Should().Equal(["service_tier=default"], "Off is the default — the owner's choice");
    }

    [Fact]
    public async Task ARowAsTheCliIsSet_IsCheckedWithNoTier()
    {
        Tiers(await CheckedArgv(",\"fast\":\"cli\"")).Should().BeEmpty();
    }
}
