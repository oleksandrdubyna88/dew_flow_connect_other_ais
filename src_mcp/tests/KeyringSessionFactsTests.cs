using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E1.S2 (DN10 of <c>todo/PLAN_kimi_and_vibe_on_native_linux.md</c>): the three keyring-session facts, each with its own value — recorded, never decided on. An unreadable process listing is "cannot tell", not "no daemon".
/// </summary>
public sealed class KeyringSessionFactsTests
{
    private sealed class Listing(ProcessListing answer) : IProcessList
    {
        public ProcessListing Snapshot() => answer;
    }

    private static Listing Running(params (string Name, string Comm)[] processes) =>
        new(new ProcessListing.Listed([.. processes.Select((process, index) => new ProcessEntry(100 + index, 1, process.Name, process.Comm))]));

    private static readonly Listing Nothing = Running();

    private static Func<string, string?> Environment(params (string Key, string Value)[] set) => key => set.FirstOrDefault(pair => pair.Key == key).Value;

    private static KeyringSessionFacts Facts(Func<string, string?> environment, Func<string, bool> isSocket, IProcessList listing, int uid = 1000) =>
        KeyringSessionFacts.Of(environment, isSocket, listing, uid);

    [Fact]
    public void Nothing_IsAllFalse_AndTheListingWasRead()
    {
        var facts = Facts(Environment(), _ => false, Nothing);

        facts.BusAddressSet.Should().BeFalse();
        facts.BusSocket.Should().BeFalse();
        facts.Daemons.Should().BeEmpty();
        facts.Listing.Should().Be(KeyringSessionFacts.Read);
    }

    [Fact]
    public void ASetBusAddress_IsRecordedAlone()
    {
        var facts = Facts(Environment(("DBUS_SESSION_BUS_ADDRESS", "unix:path=/run/user/1000/bus")), _ => false, Nothing);

        facts.BusAddressSet.Should().BeTrue("a set DBUS_SESSION_BUS_ADDRESS was recorded as unset");
        facts.BusSocket.Should().BeFalse();
        facts.Daemons.Should().BeEmpty();
    }

    [Fact]
    public void AnEmptyBusAddress_IsNotSet()
    {
        Facts(Environment(("DBUS_SESSION_BUS_ADDRESS", string.Empty)), _ => false, Nothing).BusAddressSet.Should().BeFalse("an empty DBUS_SESSION_BUS_ADDRESS was recorded as set");
    }

    [Fact]
    public void ABusSocketAtTheRuntimeDir_IsRecordedAlone()
    {
        var asked = new List<string>();
        var facts = Facts(Environment(("XDG_RUNTIME_DIR", "/run/user/1234/")), path => { asked.Add(path); return true; }, Nothing, uid: 1000);

        facts.BusSocket.Should().BeTrue("a bus socket at $XDG_RUNTIME_DIR/bus was recorded as missing");
        facts.BusAddressSet.Should().BeFalse();
        asked.Should().Equal("/run/user/1234/bus");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void WithNoRuntimeDir_TheBusIsAskedAtTheUidsRunUserFolder(string? runtimeDir)
    {
        var asked = new List<string>();
        var env = runtimeDir is null ? Environment() : Environment(("XDG_RUNTIME_DIR", runtimeDir));

        Facts(env, path => { asked.Add(path); return false; }, Nothing, uid: 1000);

        asked.Should().Equal("/run/user/1000/bus");
    }

    [Theory]
    [InlineData("gnome-keyring-daemon", "gnome-keyring-d", "gnome-keyring-daemon")]
    [InlineData("kwalletd5", "kwalletd5", "kwalletd5")]
    [InlineData("kwalletd6", "kwalletd6", "kwalletd6")]
    [InlineData("keepassxc", "keepassxc", "keepassxc")]
    [InlineData("python3", "gnome-keyring-d", "gnome-keyring-d")]
    public void AKeyringDaemonInTheListing_IsRecordedByName(string name, string comm, string recorded)
    {
        var facts = Facts(Environment(), _ => false, Running(("bash", "bash"), (name, comm), ("sshd", "sshd")));

        facts.Daemons.Should().Equal([recorded], $"a running {name} was not recorded");
        facts.Listing.Should().Be(KeyringSessionFacts.Read);
        facts.BusAddressSet.Should().BeFalse();
        facts.BusSocket.Should().BeFalse();
    }

    /// <summary>
    /// The definition of "a keyring daemon" is ONE file, <c>shared/keyring-daemon-vectors.json</c>, read by this product reading AND by the facts script's own
    /// independent reading (<c>scripts/lib/host-facts.test.mjs</c>). Every name is tried as the executable name and as the comm; a missing file fails loudly.
    /// </summary>
    public static TheoryData<string, bool> SharedDaemonVectors()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "shared", "keyring-daemon-vectors.json"));
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var data = new TheoryData<string, bool>();
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            data.Add(vector.GetProperty("name").GetString()!, vector.GetProperty("daemon").GetBoolean());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SharedDaemonVectors))]
    public void ANameIsADaemon_ExactlyWhenTheSharedDefinitionSaysSo_AsNameAndAsComm(string name, bool daemon)
    {
        var asName = Facts(Environment(), _ => false, Running((name, "other"))).Daemons;
        var asComm = Facts(Environment(), _ => false, Running(("other", name))).Daemons;

        asName.Should().Equal(daemon ? [name] : [], $"{name} (as the executable name) {(daemon ? "is" : "is not")} a keyring daemon in shared/keyring-daemon-vectors.json");
        asComm.Should().Equal(daemon ? [name] : [], $"{name} (as the comm) {(daemon ? "is" : "is not")} a keyring daemon in shared/keyring-daemon-vectors.json");
    }

    [Fact]
    public void ProcessesThatOnlyLookLikeADaemon_AreNotRecorded()
    {
        Facts(Environment(), _ => false, Running(("keepassxc-proxy", "keepassxc-proxy"), ("my-kwallet-notes", "my-kwallet-note"), ("gnome-shell", "gnome-shell"))).Daemons
            .Should().BeEmpty("a browser-integration helper or a lookalike was recorded as a keyring daemon");
    }

    [Fact]
    public void TheSameDaemonTwice_IsRecordedOnce_AndTheNamesAreSorted()
    {
        Facts(Environment(), _ => false, Running(("keepassxc", "keepassxc"), ("gnome-keyring-daemon", "gnome-keyring-d"), ("keepassxc", "keepassxc"))).Daemons
            .Should().Equal("gnome-keyring-daemon", "keepassxc");
    }

    [Fact]
    public void AnUnreadableListing_IsCannotTell_NotNoDaemon()
    {
        var facts = Facts(Environment(("DBUS_SESSION_BUS_ADDRESS", "unix:abstract=x")), _ => true, new Listing(new ProcessListing.Unreadable("/proc is hidden")));

        facts.Listing.Should().Be(KeyringSessionFacts.CannotTell, "an unreadable process listing was recorded as 'no daemon runs'");
        facts.Daemons.Should().BeEmpty();
        facts.BusAddressSet.Should().BeTrue("the other two facts stand on their own");
        facts.BusSocket.Should().BeTrue();
    }
}
