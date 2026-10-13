using System.Text.RegularExpressions;
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
/// Vibe prints in UTF-8 on every path (research/RESULTS_vibe_confinement.md §26, the first live plan round through
/// coai-mcp 0.53.0): under D17's built environment CPython 3.14 on Windows writes the console code page to a pipe, so a
/// review whose text held a <c>→</c> exited 2 — <c>'charmap' codec can't encode character '\u2192'</c> — after it was paid
/// for. Measured: <c>PYTHONUTF8=1</c> in the same environment, exit 0 and the answer <c>a → b</c>. The ONE builder
/// (<see cref="VibeLaunch.Built"/>) sets it, the launch control admits exactly that set, and the probe scripts build the same.
/// </summary>
public sealed class VibePrintsUtf8OnEveryPathTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("coai-answers-vibeutf8-");

    public void Dispose() => _dir.Dispose();

    private const string Utf8 = "PYTHONUTF8";

    private static ProcessRequest BuiltRequest() =>
        VibeLaunch.Built(new ProcessRequest("vibe", ["-p"], @"C:\w"), @"C:\h", "glm-5-3");

    private ReviewerInvocation Reviewer() =>
        new VibeRuntime("vibe", HostKind.Windows).Build(RoleCatalog.ArchitectureRole, "review", @"C:\w", "C:/s.json", _dir,
            new ReviewerSettings("vibe") { Model = "glm-5-3", Timeout = TimeSpan.FromMinutes(10), OwnedScratch = _dir.At("round") });

    private ConsultantLaunch Consult() =>
        new(@"D:\repo", "why", string.Empty, _dir, new ReviewerSettings("vibe") { Timeout = TimeSpan.FromMinutes(5), OwnedScratch = _dir.At("conv") })
        {
            Workdir = _dir.At("conv", "tree"),
        };

    [Fact]
    public async Task EveryVibePath_RunsInUtf8Mode()
    {
        var consultant = new VibeConsultant(new VibeRuntime("vibe", HostKind.Windows));
        var probe = new RecordingLauncher("python.exe C:\\Users\\p\\.local\\bin\\vibe 2.26.1\r\n");
        await new VibeRuntime("vibe", HostKind.Windows).ProbeVersionAsync(probe, "vibe", TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var question = Consult() with
        {
            Confinement = new LaunchConfinement.Planned(((Confinement.Planned)ConfinementPlanner.Plan("vibe", CapabilityGrant.None))),
            ScratchDir = _dir.At("row", "cwd"),
        };

        var paths = new Dictionary<string, ProcessRequest>
        {
            ["the reviewer"] = Reviewer().Request,
            ["a retry or a repair (ForLaunch)"] = new VibeRuntime("vibe", HostKind.Windows).ForLaunch(Reviewer()).Request,
            ["consult and the consultant check"] = consultant.Build(Consult()).Request,
            ["a question row"] = consultant.Build(question).Request,
            ["the providers probe"] = probe.Last!,
        };

        foreach (var (path, request) in paths)
        {
            request.Environment.Should().ContainKey(Utf8, $"{path}: §26 — without it a non-cp1252 character kills the run").WhoseValue.Should().Be("1", path);
            VibeLaunch.EnvironmentRefusal(request).Should().BeEmpty($"{path}: the one launch control admits what the one builder makes");
        }
    }

    [Fact]
    public void TheLaunchControl_AdmitsExactlyTheBuiltSet_AndStillRefusesAnyOtherName()
    {
        var built = BuiltRequest();
        Dictionary<string, string?> Without(string name) => built.Environment.Where(pair => pair.Key != name).ToDictionary(pair => pair.Key, pair => pair.Value);
        Dictionary<string, string?> With(string name, string value) => new(built.Environment) { [name] = value };

        VibeLaunch.EnvironmentRefusal(built).Should().BeEmpty();
        VibeLaunch.EnvironmentRefusal(built with { Environment = Without(Utf8) }).Should().NotBeEmpty("the three of 0.53.0 alone are no longer the built set");
        VibeLaunch.EnvironmentRefusal(built with { Environment = With(Utf8, "0") }).Should().NotBeEmpty("UTF-8 mode switched off is not the built set");
        VibeLaunch.EnvironmentRefusal(built with { Environment = With("PYTHONIOENCODING", "utf-8") }).Should().NotBeEmpty("no other name, Python's included");
        VibeLaunch.EnvironmentRefusal(built with { Environment = With("VIBE_MCP_SERVERS", "[]") }).Should().NotBeEmpty("§22 A");
    }

    /// <summary>
    /// The probe scripts launch with the same FIXED variables the product builds — every name but the home and the model,
    /// with its value — parsed out of <c>scripts/lib/vibe.mjs</c>'s <c>BUILT_FIXED</c> literal and compared with what
    /// <see cref="VibeLaunch.Built"/> makes (reads-another-program: a measurement harness, not one of the three programs).
    /// </summary>
    [Fact]
    public void TheProbeScripts_BuildTheSameFixedVariables()
    {
        var script = File.ReadAllText(Path.Combine(RepoPaths.Root(), "scripts", "lib", "vibe.mjs"));
        var declared = Regex.Match(script, @"export const BUILT_FIXED = \{([^}]*)\};");
        declared.Success.Should().BeTrue("scripts/lib/vibe.mjs must declare `export const BUILT_FIXED = { … };` — the probes' fixed variables");
        var probes = Regex.Matches(declared.Groups[1].Value, @"(\w+):\s*'([^']*)'").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);

        var product = BuiltRequest().Environment
            .Where(pair => pair.Key is not VibeLaunch.HomeVariable and not VibeLaunch.ModelVariable)
            .ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty);

        probes.Should().BeEquivalentTo(product, "the probes measure the launch the product makes");
        probes.Should().ContainKey(Utf8);
    }
}
