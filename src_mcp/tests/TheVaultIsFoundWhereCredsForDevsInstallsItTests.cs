using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// §9.10 of PLAN_feature_review.md: the vault is read where CredsForDevs actually installs its CLI.
/// </summary>
/// <remarks>
/// <para>Found by S0.5's probe on 2026-09-26: the CredsForDevs extension downloads <c>creds.exe</c>
/// into its own global storage (<c>…\Code\User\globalStorage\remsoftdev.creds-for-devs\bin</c>), and
/// the folder it puts on PATH holds only <c>creds-mcp.exe</c>. So a server VS Code launched ran
/// <c>creds</c> from PATH, met nothing, and answered "the `creds` CLI is not installed" — and no
/// <c>api</c> vendor ever got a key.</para>
/// <para>Every test here names its fallback explicitly and runs the FAKE CLI there: a test that let
/// the vault discover this machine's real install would read the person's real vault.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheVaultIsFoundWhereCredsForDevsInstallsItTests : IDisposable
{
    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public TheVaultIsFoundWhereCredsForDevsInstallsItTests()
    {
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"grok": "sk-live"}""");
        // A current creds CLI: its --help names the stdin marker, so it is given the key.
        Environment.SetEnvironmentVariable("FAKECLI_HELP_STDOUT", "creds config -  (" + KeyVault.StdinMarker + ")");
    }

    public void Dispose()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_EXIT", "FAKECLI_STDERR", "FAKECLI_HELP_STDOUT"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task A_creds_missing_from_PATH_is_read_from_the_extensions_own_folder()
    {
        var vault = new KeyVault(new ProcessLauncher(), "creds-binary-that-is-not-on-path", [FakeCliExe]);

        var keys = await vault.ReadFromConfigurationAsync(VaultConfiguration.Holding("cfg-live-abc"), TestContext.Current.CancellationToken);

        keys.Unavailability.Should().BeEmpty("the CLI the extension installed answers when PATH has none");
        keys.Keys["grok"].Should().Be("sk-live");
    }

    [Fact]
    public async Task PATH_still_comes_first()
    {
        // The fallback would fail if it ran: it does not exist. PATH's answer is the one taken.
        var vault = new KeyVault(new ProcessLauncher(), FakeCliExe, ["a-fallback-that-must-not-run"]);

        var keys = await vault.ReadFromConfigurationAsync(VaultConfiguration.Holding("cfg-live-abc"), TestContext.Current.CancellationToken);

        keys.Available.Should().BeTrue();
    }

    [Fact]
    public async Task Nowhere_at_all_is_still_named_and_says_where_it_looked()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "coai-no-such-dir", "creds.exe");
        var vault = new KeyVault(new ProcessLauncher(), "creds-binary-that-is-not-on-path", [elsewhere]);

        var keys = await vault.ReadFromConfigurationAsync(VaultConfiguration.Holding("cfg-live-abc"), TestContext.Current.CancellationToken);

        keys.Available.Should().BeFalse();
        keys.Unavailability.Should().Contain("not installed").And.Contain("CredsForDevs");
    }

    [Fact]
    public void Windows_looks_under_APPDATA_for_each_VS_Code_build()
    {
        var places = CredsCli.InstalledByTheExtension(
            name => name == "APPDATA" ? @"C:\Users\me\AppData\Roaming" : null, CredsCli.HostOs.Windows);

        places.Should().Contain(Path.Combine(
            @"C:\Users\me\AppData\Roaming", "Code", "User", "globalStorage", "remsoftdev.creds-for-devs", "bin", "creds.exe"));
        places.Should().Contain(p => p.Contains("Code - Insiders", StringComparison.Ordinal));
        places.Should().OnlyContain(p => p.EndsWith("creds.exe", StringComparison.Ordinal));
    }

    [Fact]
    public void MacOS_looks_under_Application_Support()
    {
        var places = CredsCli.InstalledByTheExtension(
            name => name == "HOME" ? "/Users/me" : null, CredsCli.HostOs.MacOs);

        places.Should().Contain(Path.Combine(
            "/Users/me", "Library", "Application Support", "Code", "User", "globalStorage", "remsoftdev.creds-for-devs", "bin", "creds"));
    }

    [Fact]
    public void Linux_honours_XDG_CONFIG_HOME_and_falls_back_to_dot_config()
    {
        var xdg = CredsCli.InstalledByTheExtension(
            name => name switch { "XDG_CONFIG_HOME" => "/cfg", "HOME" => "/home/me", _ => null }, CredsCli.HostOs.Linux);
        var plain = CredsCli.InstalledByTheExtension(
            name => name == "HOME" ? "/home/me" : null, CredsCli.HostOs.Linux);

        xdg.Should().Contain(Path.Combine("/cfg", "Code", "User", "globalStorage", "remsoftdev.creds-for-devs", "bin", "creds"));
        plain.Should().Contain(Path.Combine("/home/me", ".config", "Code", "User", "globalStorage", "remsoftdev.creds-for-devs", "bin", "creds"));
    }

    [Fact]
    public void No_home_to_look_in_is_no_place_rather_than_a_relative_path()
    {
        CredsCli.InstalledByTheExtension(_ => null, CredsCli.HostOs.Windows).Should().BeEmpty();
        CredsCli.InstalledByTheExtension(_ => null, CredsCli.HostOs.Linux).Should().BeEmpty();
    }

    [Fact]
    public void Only_a_place_that_exists_is_offered_to_the_vault()
    {
        var present = Path.Combine("C:", "there", "Code", "User", "globalStorage", "remsoftdev.creds-for-devs", "bin", "creds.exe");

        var offered = CredsCli.Present(
            name => name == "APPDATA" ? Path.Combine("C:", "there") : null,
            CredsCli.HostOs.Windows,
            path => path == present);

        offered.Should().Equal(present);
    }
}
