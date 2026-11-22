using Xunit;
using FluentAssertions;
using CoaiMcp.Server;

namespace CoaiMcp.Tests;

/// <summary>
/// A switch flipped a second ago governs the next call — not the next restart.
/// </summary>
/// <remarks>
/// <para>The operator's requirement, in their own words: "if I changed a setting one second before
/// Claude calls the MCP, it must already be working with the new one." The mechanism exists —
/// <see cref="PanelServiceHost"/> stamps the settings file by its write time and its length and
/// rebuilds the service when either changes — and this is what says so out loud, because a
/// reload that quietly stops working looks exactly like a setting that had no effect.</para>
/// <para>The panel writes that file the moment a box is ticked, so the two halves meet at the file:
/// the extension's own test walks every field into it, and this one proves the server picks the
/// file up on the very next call.</para>
/// </remarks>
public sealed class SettingsAreLiveTests : IDisposable
{
    private readonly string _dir = TempDir.For("coai-live-").Path;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private void Write(string json) => File.WriteAllText(SettingsPath, json);

    private PanelServiceHost Host()
    {
        var env = new Dictionary<string, string?>
        {
            ["COAI_DATA_DIR"] = _dir,
            ["COAI_PROVIDERS"] = "codex",
        };

        return new PanelServiceHost(
            name => env.GetValueOrDefault(name),
            VaultKeys.None("no vault in this test"),
            default,
            new Runners.Processes.ProcessLauncher(),
            Serilog.Log.Logger, Noticing.None, CancellationToken.None);
    }

    [Fact]
    public void ASettingChangedASecondAgo_GovernsTheNextCall()
    {
        Write("""{ "COAI_CONS_TURNS": "3" }""");
        var host = Host();

        host.Current.Settings.ConsultTurns.Should().Be(3);

        // A second later, because that is the operator's own example — and because the stamp is the
        // file's write time and length, which a same-second rewrite of the same length would not
        // change. One second is longer than any editor takes to save.
        Thread.Sleep(1100);
        Write("""{ "COAI_CONS_TURNS": "4" }""");

        host.Current.Settings.ConsultTurns.Should().Be(4, "the next call reads the file, not the memory of it");
    }

    [Fact]
    public void ASwitchTickedASecondAgo_GovernsTheNextCall_AndSwitchingItBackOffIsLiveToo()
    {
        // The direction nobody tests: a reload that only ever adds is a reload that cannot take anything away.
        Write("""{ "COAI_CONS_ENABLED": "false" }""");
        var host = Host();
        host.Current.Settings.ConsultEnabled.Should().BeFalse();

        Thread.Sleep(1100);
        Write("{}");

        host.Current.Settings.ConsultEnabled.Should().BeTrue("a key removed from the file is the default again");
    }

    [Fact]
    public void NoFileAtAll_IsTheDefaults_AndCreatingOneCounts()
    {
        var host = Host();
        host.Current.Settings.ConsultTurns.Should().Be(5, "nothing is changed until somebody changes it");

        Thread.Sleep(1100);
        Write("""{ "COAI_CONS_TURNS": "2" }""");

        host.Current.Settings.ConsultTurns.Should().Be(2, "a file that did not exist and now does is a change");
    }
}
