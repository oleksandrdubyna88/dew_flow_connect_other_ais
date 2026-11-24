using CoaiMcp.Install;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>t1 of the installer story against real child processes: the wrapper files of a scratch world answer <c>--version</c>, and one that does not answer is still found.</summary>
public sealed class ClientFinderOnTheWrappersTests
{
    [Fact]
    public async Task ACliThatDoesNotAnswerItsVersion_IsStillFound_WithNoVersion_AndALimitedWait()
    {
        using var world = InstallWorld.Create(codex: false);
        world.Steer(Client.Claude, hang: "version");
        var host = InstallHost.From(name => world.Env.GetValueOrDefault(name), OperatingSystem.IsWindows());
        var finder = new ClientFinder(host, ExecutableResolver.IsInstalledFile, new ProcessLauncher(), versionTimeout: TimeSpan.FromSeconds(1.5));

        var found = (await finder.FindAsync([Client.Claude], CancellationToken.None)).Single();

        found.Found.Should().BeTrue();
        found.Version.Should().BeEmpty();
        found.Reason.Should().Contain("did not answer");
    }

    [Fact]
    public async Task TheWrappers_AnswerTheirVersion()
    {
        using var world = InstallWorld.Create();
        var host = InstallHost.From(name => world.Env.GetValueOrDefault(name), OperatingSystem.IsWindows());
        var finder = new ClientFinder(host, ExecutableResolver.IsInstalledFile, new ProcessLauncher());

        var found = await finder.FindAsync(Clients.All, CancellationToken.None);

        found.Select(one => (one.Cli, one.Found, one.Version)).Should().Equal(
            (Client.Claude, true, "2.1.330 (Claude Code)"), (Client.Codex, true, "codex-cli 0.171.0"), (Client.Agy, false, string.Empty));
    }
}
