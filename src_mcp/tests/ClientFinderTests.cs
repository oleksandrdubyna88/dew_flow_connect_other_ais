using CoaiMcp.Install;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>t1 of the installer story: the CLIs are found on THIS side (PATH first, the user's bin folder as the fallback), an absent one is reported absent, and a version is asked, not assumed.</summary>
public sealed class ClientFinderTests
{
    private static InstallHost Posix(string path, string home = "/home/p") =>
        InstallHost.From(name => name switch { "HOME" => home, "PATH" => path, _ => null }, isWindows: false);

    [Fact]
    public void AnAbsentCli_IsReportedAbsent_NotGuessed()
    {
        var finder = new ClientFinder(Posix("/usr/bin"), _ => false, new FakeLauncher());

        Clients.All.Select(finder.PathOf).Should().OnlyContain(path => path.Length == 0);
    }

    [Fact]
    public void TheCliOnThePath_Wins_OverTheOneInTheUsersBinFolder()
    {
        var present = new HashSet<string> { "/usr/bin/claude", "/home/p/.local/bin/claude", "/home/p/.local/bin/agy" };
        var finder = new ClientFinder(Posix("/usr/bin"), present.Contains, new FakeLauncher());

        finder.PathOf(Client.Claude).Should().Be("/usr/bin/claude");
        finder.PathOf(Client.Agy).Should().Be("/home/p/.local/bin/agy", "an editor-started process has a PATH without the home bin; the fallback finds it");
        finder.PathOf(Client.Codex).Should().BeEmpty();
    }

    [Fact]
    public async Task AFoundCli_IsAskedItsVersion_ThroughTheLauncher_FirstLineOnly()
    {
        var finder = new ClientFinder(Posix("/usr/bin"), path => path == "/usr/bin/claude", new FakeLauncher("2.1.330 (Claude Code)\nsecond line\n"));

        var found = await finder.FindAsync([Client.Claude, Client.Codex], CancellationToken.None);

        found[0].Should().BeEquivalentTo(new FoundClient(Client.Claude, true, "/usr/bin/claude", "2.1.330 (Claude Code)", string.Empty));
        found[1].Found.Should().BeFalse();
        found[1].Version.Should().BeEmpty();
    }
}
