using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The Kimi shim as it actually runs (S3.2, S3.14, S3.15 and carries (a)/(b) of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): the REAL
/// <c>coai-mcp --ask-acp --profile kimi</c> in its own process, its agent the fake CLI, on an owned home in the test's own folder whose
/// <c>credentials</c> is a junction to a fake person's.
/// </summary>
/// <remarks>
/// <para><b>The shim is a child, never the test host</b> — what it takes is an INHERITABLE hold, and an inheritable handle in the test host
/// would be lent to every process the seven parallel tests start (<see cref="NoTestHoldsAnInheritableHandleTests"/>). The test re-reads the two
/// lock files only with the non-inheriting <see cref="HeldFile.Probe"/>.</para>
/// <para><b>The agent sees what the shim built.</b> It is started with its environment BUILT from scratch, so no <c>FAKECLI_*</c> variable
/// reaches it: the fake agent finds its script beside its working folder, writes its own environment and probes the two lock files at start
/// (<see cref="FakeAcpAgent.EnvField"/>) — the hold is observed from where Kimi would be, inside the tree.</para>
/// </remarks>
public sealed class AKimiShimScenarioTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly KimiFixture _person = new();

    public void Dispose() => _person.Dispose();

    private string Locks => Path.Combine(_person.Folder, "locks");

    private static void OnWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Kimi runs on Windows hosts only (D18)");

    private (string Tree, string Home) HoldFiles(KimiPlaces places)
    {
        using var rooted = KimiCredentialsLock.RootFor(Locks);

        return (KimiCredentialsLock.TreePathFor(_person.Credentials), Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName));
    }

    private ProcessRequest ShimRequest(KimiPlaces places, FakeAcpAgent agent, IReadOnlyDictionary<string, string?> environment, string deadlineSeconds = "60") =>
        new(
            ServerBinary.Path,
            [
                "--ask-acp", "--profile", "kimi", "--executable", FakeAcpAgent.FakeCliExe, "--model", "kimi-code/k3", "--cwd", agent.Cwd,
                "--deadline-seconds", deadlineSeconds, "--home", places.OwnedRoot, "--persons-bin", places.PersonsBin, "--locks", Locks,
            ],
            agent.Cwd)
        {
            StdIn = "say hello",
            Timeout = TimeSpan.FromMinutes(2),
            Environment = new Dictionary<string, string?>(environment),
        };

    private static Task<ProcessResult> RunAsync(ProcessRequest request) => new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

    private static FakeAcpAgent AgentFor(params string[] extra) => new([FakeAcpAgent.Chunk("done")], FakeAcpAgent.Models("kimi-code/k3") + string.Concat(extra));

    /// <summary>The agent's environment as it wrote it: names to values (compared as Windows compares names), and the probe lines.</summary>
    private static (Dictionary<string, string> Variables, Dictionary<string, string> Probes) ReadEnvFile(FakeAcpAgent agent)
    {
        File.Exists(agent.EnvFile).Should().BeTrue("the agent writes its environment before it reads a line");
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var probes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(agent.EnvFile))
        {
            if (line.StartsWith("PROBE ", StringComparison.Ordinal))
            {
                var at = line.LastIndexOf(' ');
                probes[line[6..at]] = line[(at + 1)..];
            }
            else if (line.IndexOf('=', 1) is var equals and > 0)
            {
                variables[line[..equals]] = line[(equals + 1)..];
            }
        }

        return (variables, probes);
    }

    // ---------- S3.2 ----------

    [Fact]
    public async Task TheAgentGetsTheBuiltEnvironment_NotTheParents()
    {
        OnWindows();
        var places = _person.MadeHome("kh-env");
        using var agent = AgentFor();
        using var elsewhere = TempDir.For("testsToDelete-kimi-elsewhere-");
        agent.WithFields(agent.EnvField());
        var request = ShimRequest(places, agent, new Dictionary<string, string?>
        {
            ["KIMI_CODE_HOME"] = elsewhere.Path,
            ["VIBE_MCP_SERVERS"] = "x",
            ["USERPROFILE"] = Environment.GetEnvironmentVariable("USERPROFILE"),
        });

        var result = await RunAsync(request);

        result.ExitCode.Should().Be(0, result.StdErr);
        var (variables, _) = ReadEnvFile(agent);
        variables.ContainsKey("VIBE_MCP_SERVERS").Should().BeFalse("the parent’s environment is not inherited (its names are never listed in a failure: they hold tokens)");
        variables["KIMI_CODE_HOME"].Should().Be(places.Home, "the owned home, not the parent’s " + elsewhere.Path);
        variables["KIMI_CODE_NO_AUTO_UPDATE"].Should().Be("1");
        variables["USERPROFILE"].Should().Be(places.Os);
        variables["HOME"].Should().Be(places.Os);
        (variables["HOMEDRIVE"] + variables["HOMEPATH"]).Should().Be(places.Os);
        variables["PATH"].Split(Path.PathSeparator)[0].Should().Be(places.PersonsBin, "the person’s own bin comes first");
        var allowed = ProcessEnvironment.Minimal.Concat(["KIMI_CODE_HOME", "KIMI_CODE_NO_AUTO_UPDATE", "HOME"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        variables.Keys.Where(name => !allowed.Contains(name)).Should().BeEmpty("only the minimal names and the seven the launch builds");
    }

    // ---------- S3.14 ----------

    [Fact]
    public async Task TheTreeHold_IsTakenBeforeTheStart_AndFreeAfterANormalTurn()
    {
        OnWindows();
        var places = _person.MadeHome("kh-hold");
        var (tree, home) = HoldFiles(places);
        using var agent = AgentFor();
        agent.WithFields(agent.EnvField(tree, home));

        var result = await RunAsync(ShimRequest(places, agent, new Dictionary<string, string?>()));

        result.ExitCode.Should().Be(0, result.StdErr);
        JsonDocument.Parse(result.StdOut).RootElement.GetProperty("end").GetString().Should().Be("answered");
        var (_, probes) = ReadEnvFile(agent);
        probes[tree].Should().Be("Held", "the agent started inside the sign-in’s tree hold — it inherited it");
        probes[home].Should().Be("Held", "and inside the home’s");
        (await Polls.Until(() => HeldFile.Probe(tree) == LockProbe.Free && HeldFile.Probe(home) == LockProbe.Free, Patience))
            .Should().BeTrue("a normal turn ends with the agent gone and the shim exited, so both files are free");
    }

    // ---------- carry (b): the shim reads no environment ----------

    [Fact]
    public async Task TheShimReadsNoEnvironment_InAChildWithARedirectedProfile()
    {
        OnWindows();
        var places = _person.MadeHome("kh-redirected");
        var (tree, home) = HoldFiles(places);
        using var decoy = TempDir.For("testsToDelete-kimi-decoy-");
        using var agent = AgentFor();
        agent.WithFields(agent.EnvField(tree, home));
        var redirected = new Dictionary<string, string?>
        {
            ["USERPROFILE"] = decoy.Path,
            ["HOME"] = decoy.Path,
            ["HOMEDRIVE"] = Path.GetPathRoot(decoy.Path)!.TrimEnd('\\'),
            ["HOMEPATH"] = decoy.Path[Path.GetPathRoot(decoy.Path)!.TrimEnd('\\').Length..],
            ["LOCALAPPDATA"] = decoy.At("local"),
            ["APPDATA"] = decoy.At("roaming"),
            ["KIMI_CODE_HOME"] = decoy.At("kimi-home"),
            ["KIMI_INSTALL_DIR"] = decoy.At("kimi-install"),
        };

        var result = await RunAsync(ShimRequest(places, agent, redirected));

        result.ExitCode.Should().Be(0, "the shim found everything it needs on its argv: " + result.StdErr);
        var (variables, probes) = ReadEnvFile(agent);
        probes[tree].Should().Be("Held", "the key came from the owned junction’s own reparse data, not from a profile");
        probes[home].Should().Be("Held");
        variables["KIMI_CODE_HOME"].Should().Be(places.Home);
        variables["USERPROFILE"].Should().Be(places.Os, "the agent’s profile is the owned os/, whatever the shim’s was");
        Directory.EnumerateFileSystemEntries(decoy.Path).Should().BeEmpty("nothing was looked up, made or written in any place the environment names");
        Directory.EnumerateFiles(Locks).Select(Path.GetFileName).Should().Contain(Path.GetFileName(tree), "the lock folder is the --locks flag’s");
    }

    // ---------- S3.15 ----------

    [Fact]
    public async Task ATreeHoldThatIsBusyPastTheDeadline_Exits69WithTheSentence()
    {
        OnWindows();
        var places = _person.MadeHome("kh-busy");
        var (tree, _) = HoldFiles(places);
        var elsewhere = Path.Combine(_person.Folder, "another-home");
        Directory.CreateDirectory(elsewhere);
        using var agent = AgentFor();
        var (holder, grandchild) = await KimiCredentialsLockTests.StartShimAsync("tree", Locks, _person.Credentials, elsewhere);
        try
        {
            var asked = Stopwatch.StartNew();

            var result = await RunAsync(ShimRequest(places, agent, new Dictionary<string, string?>(), deadlineSeconds: "3"));

            asked.Stop();
            result.ExitCode.Should().Be(69, result.StdErr);
            result.StdErr.Should().Contain("another Kimi started on this sign-in is still running").And.Contain(tree);
            JsonDocument.Parse(result.StdOut).RootElement.GetProperty("end").GetString().Should().Be("not_started");
            File.Exists(agent.Log).Should().BeFalse("the agent never started beside the holder");
            asked.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2), "the shim waited out its deadline for the holder").And.BeLessThan(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await KimiCredentialsLockTests.EndAsync(holder);
            await KimiCredentialsLockTests.EndAsync(grandchild);
        }
    }

    [Fact]
    public async Task AHomeWithNoCredentialsJunction_Exits65_NamingIt_AndHoldsNothing()
    {
        OnWindows();
        var places = _person.Places("kh-nolink");
        Directory.CreateDirectory(places.Home);
        Directory.CreateDirectory(places.Os);
        using var agent = AgentFor();

        var result = await RunAsync(ShimRequest(places, agent, new Dictionary<string, string?>()));

        result.ExitCode.Should().Be(65, "there is no sign-in to hold, so there is nothing to start the agent under: " + result.StdErr);
        result.StdErr.Should().Contain("no credentials junction").And.Contain(places.Link);
        File.Exists(agent.Log).Should().BeFalse();
        Directory.Exists(Locks).Should().BeFalse("no lock folder was made for a sign-in that could not be named");
    }

    [Fact]
    public async Task ALockFolderThatCannotBeUsed_Exits65()
    {
        OnWindows();
        var places = _person.MadeHome("kh-unusable");
        File.WriteAllText(Locks, "not a folder");
        using var agent = AgentFor();

        var result = await RunAsync(ShimRequest(places, agent, new Dictionary<string, string?>()));

        result.ExitCode.Should().Be(65, "a lock that cannot be taken is a request this shim will not make — there is no warning-only fallback: " + result.StdErr);
        File.Exists(agent.Log).Should().BeFalse();
    }
}
