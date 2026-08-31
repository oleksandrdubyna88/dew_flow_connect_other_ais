using Xunit;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

public sealed class KeyVaultParseTests
{
    [Fact]
    public void ValidObject_YieldsTheKeys()
    {
        var keys = KeyVault.Parse("""{"deepseek": "sk-a", "openrouter": "sk-b"}""");

        keys.Available.Should().BeTrue();
        keys.Keys.Should().HaveCount(2).And.ContainKey("deepseek");
    }

    [Fact]
    public void CaseOfTheVendorName_DoesNotMatter() =>
        KeyVault.Parse("""{"DeepSeek": "sk-a"}""").Keys.Should().ContainKey("deepseek");

    [Fact]
    public void NotAnObject_IsANamedCondition()
    {
        var keys = KeyVault.Parse("""["sk-a"]""");

        keys.Available.Should().BeFalse();
        keys.Unavailability.Should().Contain("not an object");
        keys.Keys.Should().BeEmpty("never a partial apply");
    }

    [Fact]
    public void MalformedJson_IsANamedCondition() =>
        KeyVault.Parse("{oops").Unavailability.Should().Contain("not valid JSON");

    [Fact]
    public void EmptyValues_AreSkipped_NotApplied() =>
        KeyVault.Parse("""{"deepseek": ""}""").Keys.Should().BeEmpty();
}

[Collection("fakecli-env")]
public sealed class KeyVaultProcessTests : IDisposable
{
    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private readonly KeyVault _vault = new(new ProcessLauncher(), FakeCliExe);

    public KeyVaultProcessTests() => Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");

    public void Dispose()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public async Task NoKeyConfigured_IsNamed_AndKeylessVendorsStillWork()
    {
        var keys = await _vault.ReadAsync(null, TestContext.Current.CancellationToken);

        keys.Available.Should().BeFalse();
        keys.Unavailability.Should().Contain("keyless vendors still work");
    }

    [Fact]
    public async Task RefusedKey_IsNamed_NeverAnException()
    {
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "1");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "401");

        var keys = await _vault.ReadAsync("cfg-live-abc", TestContext.Current.CancellationToken);

        keys.Available.Should().BeFalse();
        keys.Unavailability.Should().Contain("revoked");
    }

    [Fact]
    public async Task ServedConfig_YieldsTheKeys()
    {
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"deepseek": "sk-live"}""");

        var keys = await _vault.ReadAsync("cfg-live-abc", TestContext.Current.CancellationToken);

        keys.Available.Should().BeTrue();
        keys.Keys["deepseek"].Should().Be("sk-live");
    }

    [Fact]
    public async Task MissingCredsBinary_IsNamed()
    {
        var vault = new KeyVault(new ProcessLauncher(), "creds-binary-that-does-not-exist");

        var keys = await vault.ReadAsync("cfg-live-abc", TestContext.Current.CancellationToken);

        keys.Available.Should().BeFalse();
        keys.Unavailability.Should().Contain("not installed");
    }
}
