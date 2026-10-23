using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S-N23 of <c>todo/PLAN_kimi_and_vibe_on_native_linux_E2.md</c> (the C1 risk consult 8e2480a0, point 4): DELIVERY, not a request dictionary. A REAL child, started by the real
/// <see cref="ProcessLauncher"/>, observes <c>PYTHON_KEYRING_BACKEND=keyring.backends.fail.Keyring</c> on every road a native Linux Vibe child is made by — the review launch, the retry, the consult,
/// the question row, the <c>providers</c> probe's <c>--version</c> and the launch control's own <c>--version</c> — while THIS process holds a CONFLICTING value; a launch missing the fifth entry or carrying
/// a wrong one is refused BEFORE a child exists (the fake's record file is never written); and on WSL2 the same six roads leak nothing.
/// </summary>
/// <remarks>
/// <para>The child is the fake CLI (<c>FakeCli</c> beside the test binary), whose environment record writes every <c>VIBE_*</c> and <c>PYTHON*</c> name WITH its value — what crossed the process boundary, not what a
/// dictionary said. Its <c>--version</c> children record into their own file (<c>FAKECLI_VERSION_RECORD_ENV</c>): a version probe returns before a launch's record is ever written.</para>
/// <para><b>Linux only</b> — by a skip reason elsewhere, so the test is named in every other leg's report. The sign-in link is a real symbolic link to a 0600 file this process owns
/// (<see cref="VibeFlowHost"/>); the Linux CI job is the leg that runs it. What this does not prove: a real Vibe, a real keyring, the person's real <c>~/.vibe/.env</c>.</para>
/// <para>In the <c>fakecli-env</c> collection: the child is steered by the file beside the fake's binary, and this class sets a process environment variable for the duration of each test (restored).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class VibeNativeLinuxDeliveryTests
{
    private const string Backend = VibeLaunch.KeyringBackendVariable;

    private const string Closed = VibeLaunch.KeyringBackendClosed;

    private const string TheServers = "keyring.backends.null.Keyring";

    private const string Review = "the review launch";
    private const string Retry = "the retry or repair (ForLaunch)";
    private const string Consult = "the consult";
    private const string Question = "the question row";
    private const string Probe = "the providers --version probe";
    private const string Take = "the launch control's own --version";

    private static readonly string[] Roads = [Review, Retry, Consult, Question, Probe, Take];

    public static TheoryData<string, string> NativeRoads => RoadsOn(VibeFlowHost.NativeLinux);

    public static TheoryData<string, string> Wsl2Roads => RoadsOn(VibeFlowHost.Wsl2);

    public static TheoryData<string> Refusals => new() { "missing", "wrong" };

    private static TheoryData<string, string> RoadsOn(string mode)
    {
        var data = new TheoryData<string, string>();
        foreach (var road in Roads)
        {
            data.Add(mode, road);
        }

        return data;
    }

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private static void OnLinux() => Assert.SkipUnless(OperatingSystem.IsLinux(), "a real child, a real symbolic link and a 0600 sign-in: the Linux CI job runs this (S-N23)");

    /// <summary>S-N23: on a native Linux host every road delivers coai's closed backend to a real child, never the server's own value, never nothing.</summary>
    [Theory]
    [MemberData(nameof(NativeRoads))]
    public async Task OnANativeLinuxHost_EveryRoad_DeliversTheClosedKeyringBackend_DespiteTheServersOwnValue(string mode, string road)
    {
        OnLinux();
        using var scene = Scene.Begin(mode);

        await scene.RunAsync(road);

        foreach (var (what, lines) in scene.Observed(road))
        {
            lines.Should().Contain($"{Backend}={Closed}", Because(road, what));
            lines.Should().NotContain($"{Backend}={TheServers}", $"{road}: the {what} child carried the server's own PYTHON_KEYRING_BACKEND, not coai's");
        }
    }

    /// <summary>S-N23, the refusal half: a request missing the fifth entry, or carrying a wrong one, is refused BEFORE spawning — no child existed, so the fake wrote nothing.</summary>
    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task OnANativeLinuxHost_ALaunchMissingOrWrongInTheFifthEntry_IsRefused_BeforeAnyChildExists(string how)
    {
        OnLinux();
        using var scene = Scene.Begin(VibeFlowHost.NativeLinux);
        var built = scene.Invocation().Request;
        var changed = how == "missing"
            ? built.Environment.Where(pair => pair.Key != Backend).ToDictionary(pair => pair.Key, pair => pair.Value)
            : new Dictionary<string, string?>(built.Environment) { [Backend] = TheServers };

        using var turn = await VibeLaunch.TakeAsync(scene.Flow.Host, built with { Environment = changed }, FakeVibe.ExportOf(built), new ProcessLauncher(), VibePathBudget.None, TestContext.Current.CancellationToken);

        turn.Refusal.Should().Contain("D17").And.Contain($"{Backend}={Closed}", $"a Vibe launch with a {how} {Backend} on a native Linux host was admitted");
        File.Exists(scene.VersionRecord).Should().BeFalse($"a Vibe launch {how} {Backend} on a native Linux host SPAWNED before it was refused — the fake wrote its record file");
        File.Exists(scene.LaunchRecord).Should().BeFalse("no review ran either");
        Directory.GetFiles(scene.Dir.At("argv"), "*.argv").Should().BeEmpty("no child of any kind existed");
    }

    /// <summary>S-N23, the WSL2 leg: the closed road never leaks — the same six roads on a WSL2 host leave NO <c>PYTHON_KEYRING_BACKEND</c> line in any record, even with the conflicting server value set.</summary>
    [Theory]
    [MemberData(nameof(Wsl2Roads))]
    public async Task OnWsl2_EveryRoad_CarriesNoKeyringBackend_EvenWithTheServersOwnValueSet(string mode, string road)
    {
        OnLinux();
        using var scene = Scene.Begin(mode);

        await scene.RunAsync(road);

        foreach (var (what, lines) in scene.Observed(road))
        {
            lines.Should().NotContain(line => line.StartsWith(Backend, StringComparison.Ordinal), $"{road}: a WSL2 Vibe {what} child carried PYTHON_KEYRING_BACKEND — a measured launch was changed");
            lines.Should().Contain("PYTHONUTF8=1", $"{road}: the {what} child was observed at all (its record is the proof)");
        }
    }

    private static string Because(string road, string what) => road switch
    {
        Probe => "the providers --version probe on a native Linux host reached the child WITHOUT keyring.backends.fail.Keyring (it carried keyring.backends.null.Keyring from the server's own environment) — the one road that bypasses TakeAsync",
        Consult => $"the consult {what} child on a native Linux host carried no PYTHON_KEYRING_BACKEND",
        _ => $"{road}: the {what} child on a native Linux host carried no PYTHON_KEYRING_BACKEND",
    };

    /// <summary>One road's world: a host (a native Linux or a WSL2 one, a real sign-in link in its own scratch), the fake CLI's steering, and the server's conflicting value in THIS process.</summary>
    private sealed class Scene : IDisposable
    {
        private readonly string? _before = Environment.GetEnvironmentVariable(Backend);
        private readonly IDisposable _steering;
        private readonly VibeFlowHost _flow;

        private Scene(TempDir dir, VibeFlowHost flow, IDisposable steering)
        {
            Dir = dir;
            _flow = flow;
            _steering = steering;
        }

        public TempDir Dir { get; }

        public VibeFlowHost Flow => _flow;

        public string VersionRecord => Dir.At("version-env.txt");

        public string LaunchRecord => Dir.At("launch-env.txt");

        public static Scene Begin(string mode)
        {
            FakeCliSteering.Reset();
            var dir = TempDir.For("coai-answers-vnld-");
            Directory.CreateDirectory(dir.At("argv"));
            var flow = VibeFlowHost.Enter(mode, dir.At("persons"));
            var steering = FakeCliSteering.Write(new Dictionary<string, string>
            {
                ["FAKECLI_STDOUT"] = FakeVibe.Answer,
                ["FAKECLI_EXPORT_TEXT"] = """{"outcome":"finished","usage":{"input_tokens":1200,"output_tokens":34,"cached_input_tokens":0,"total_tokens":1234},"cost_usd":0.01,"config":{"active_model":"{{env:VIBE_ACTIVE_MODEL}}"}}""",
                ["FAKECLI_VERSION_STDOUT"] = "python.exe /home/p/.local/bin/vibe 2.26.1\n",
                ["FAKECLI_RECORD_ENV"] = dir.At("launch-env.txt"),
                ["FAKECLI_VERSION_RECORD_ENV"] = dir.At("version-env.txt"),
                ["FAKECLI_RECORD_DIR"] = dir.At("argv"),
            });
            var scene = new Scene(dir, flow, steering);
            // The CONFLICTING value, in the server's own environment, for the duration: a launch that inherits it (or reads it) delivers the wrong backend.
            Environment.SetEnvironmentVariable(Backend, TheServers);

            return scene;
        }

        private VibeRuntime Runtime => new("vibe", _flow.Host, VibePathBudget.None);

        private ReviewerSettings Settings(string owned) =>
            new("vibe") { ExecutablePath = FakeCliExe, Model = "glm-5-3", OwnedScratch = Directory.CreateDirectory(Dir.At(owned)).FullName, Timeout = TimeSpan.FromSeconds(60) };

        public ReviewerInvocation Invocation() =>
            Runtime.Build(RoleCatalog.ArchitectureRole, "review this", Directory.CreateDirectory(Dir.At("work")).FullName, Dir.At("s.json"), Dir, Settings("round"));

        private ReviewerInvocation ConsultInvocation(bool question)
        {
            var launch = new ConsultantLaunch("/repo", "why", string.Empty, Dir, Settings("conv")) { Workdir = Directory.CreateDirectory(Dir.At("conv", "tree")).FullName };
            if (question)
            {
                launch = launch with
                {
                    Confinement = new LaunchConfinement.Planned((Confinement.Planned)ConfinementPlanner.Plan("vibe", CapabilityGrant.None)),
                    ScratchDir = Directory.CreateDirectory(Dir.At("row", "cwd")).FullName,
                };
            }

            return new VibeConsultant(Runtime).Build(launch);
        }

        /// <summary>Runs one road against the real launcher; the fake's records are what the test reads afterwards.</summary>
        public async Task RunAsync(string road)
        {
            var ct = TestContext.Current.CancellationToken;
            var launcher = new ProcessLauncher();
            switch (road)
            {
                case Probe:
                    await Runtime.ProbeVersionAsync(launcher, FakeCliExe, TimeSpan.FromSeconds(30), ct);
                    break;
                case Take:
                    var request = Invocation().Request;
                    using (var turn = await VibeLaunch.TakeAsync(_flow.Host, request, FakeVibe.ExportOf(request), launcher, VibePathBudget.None, ct))
                    {
                        turn.Refusal.Should().BeEmpty("the launch control's own --version needs the launch admitted");
                    }

                    break;
                default:
                    await new ReviewerExecutor(launcher).RunAsync(InvocationFor(road), ct: ct);
                    break;
            }
        }

        private ReviewerInvocation InvocationFor(string road) => road switch
        {
            Retry => Runtime.ForLaunch(Invocation()),
            Consult => ConsultInvocation(question: false),
            Question => ConsultInvocation(question: true),
            _ => Invocation(),
        };

        /// <summary>The records a road leaves: a version child always; a launch child too unless the road IS a version probe.</summary>
        public IEnumerable<(string What, string[] Lines)> Observed(string road)
        {
            File.Exists(VersionRecord).Should().BeTrue($"{road}: no --version child recorded its environment — the road never reached a child");
            yield return ("--version", File.ReadAllLines(VersionRecord));
            if (road is Probe or Take)
            {
                yield break;
            }

            File.Exists(LaunchRecord).Should().BeTrue($"{road}: no launch child recorded its environment — the road never reached a child");
            yield return ("launch", File.ReadAllLines(LaunchRecord));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Backend, _before);
            _steering.Dispose();
            _flow.Dispose();
            Dir.Dispose();
        }
    }
}
