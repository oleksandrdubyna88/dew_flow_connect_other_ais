using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What `providers` — and, later, the Team server's catalog — says about a vendor before anybody
/// trusts it with a round.
/// </summary>
/// <remarks>
/// Driven through the real fake CLI rather than a mocked launcher: the thing being tested is what a
/// process's exit code and streams MEAN, and a mock would let the test agree with itself.
/// </remarks>
[Collection("fakecli-env")]
public sealed class VendorProbeTests : IDisposable
{
    private readonly IProcessLauncher _launcher = new ProcessLauncher();

    public VendorProbeTests()
    {
        // Vendor mode, so the stand-in answers `--version` like a CLI instead of refusing an
        // unknown verb.
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", "fake-cli 9.9.9");
    }

    public void Dispose()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_SLEEP_MS"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static VendorIdentity Vendor(string provider, string runtime = "", string baseUrl = "") =>
        new(provider, runtime, baseUrl);

    private Task<VendorHealth> Probe(
        VendorIdentity vendor,
        string exe = "",
        string model = "",
        bool enabled = true,
        bool hasKey = false,
        TimeSpan? timeout = null) =>
        VendorProbe.RunAsync(_launcher, vendor, enabled, exe, model, hasKey, TestContext.Current.CancellationToken, timeout);

    [Fact]
    public async Task ACliThatAnswers_IsFound_WithWhatItSaid()
    {
        var health = await Probe(Vendor("codex"), exe: FakeCliInvocations.Exe);

        health.CliFound.Should().BeTrue();
        health.Version.Should().Be("fake-cli 9.9.9", "the version a person reads is the CLI's own words");
        health.Auth.Should().Be("own auth");
    }

    [Fact]
    public async Task AnExecutableThatIsNotThere_IsNotFound_AndSaysWhereToGetIt()
    {
        var health = await Probe(Vendor("codex"), exe: Path.Combine(Path.GetTempPath(), "no-such-cli-xyz"));

        health.CliFound.Should().BeFalse();
        health.Note.Should().Contain("was not found on this machine");
    }

    /// <summary>
    /// A CLI that never answers is its own diagnosis, not an exit code nobody can act on.
    /// </summary>
    /// <remarks>
    /// Asked for on this story's plan round by all three reviewers, from three angles. Before this,
    /// a hung probe fell through to the exit-code arm and reported a number produced by the kill
    /// rather than by the vendor.
    /// </remarks>
    [Fact]
    public async Task ACliThatHangs_IsReportedAsSilent_NotAsAnExitCode()
    {
        Environment.SetEnvironmentVariable("FAKECLI_SLEEP_MS", "5000");

        var health = await Probe(
            Vendor("codex"),
            exe: FakeCliInvocations.Exe,
            timeout: TimeSpan.FromMilliseconds(400));

        health.Note.Should().Contain("did not answer --version",
            "a killed process's exit code is a number about the kill, not about the vendor");
        health.Version.Should().BeEmpty();
    }

    [Fact]
    public async Task ACliThatRefuses_CarriesItsOwnWords()
    {
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "3");
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "the vendor said no");
        try
        {
            var health = await Probe(Vendor("codex"), exe: FakeCliInvocations.Exe);

            health.CliFound.Should().BeTrue("it started; it simply refused");
            health.Note.Should().Contain("exited 3").And.Contain("the vendor said no");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_EXIT", null);
            Environment.SetEnvironmentVariable("FAKECLI_STDERR", null);
        }
    }

    /// <summary>
    /// A CLI that writes its refusal to STDOUT still gets a note with words in it.
    /// </summary>
    /// <remarks>
    /// Found by gemini on this change's code round: reading stderr alone left the note as
    /// "--version exited 1: " — an exit code, a colon, and nothing — for every vendor that
    /// diagnoses itself on the other stream.
    /// </remarks>
    [Fact]
    public async Task ARefusalOnStdout_IsNotLost()
    {
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "1");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", "this vendor explains itself on stdout");
        try
        {
            var health = await Probe(Vendor("codex"), exe: FakeCliInvocations.Exe);

            health.Note.Should().Contain("this vendor explains itself on stdout");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_EXIT", null);
            Environment.SetEnvironmentVariable("FAKECLI_STDOUT", "fake-cli 9.9.9");
        }
    }

    [Fact]
    public async Task AVendorTurnedOff_SaysSo_AndIsNotProbed()
    {
        var health = await Probe(Vendor("codex"), exe: FakeCliInvocations.Exe, enabled: false);

        health.Enabled.Should().BeFalse();
        health.Note.Should().Be("disabled in settings");
    }

    [Fact]
    public async Task AProviderNobodyKnows_IsRefused_NamingTheCatalog()
    {
        var health = await Probe(Vendor("acme"));

        health.Auth.Should().Be("unavailable");
        health.Note.Should().Contain("acme").And.Contain("this build knows");
    }

    /// <summary>
    /// A retired runtime is answered BEFORE the probe, because `gemini --version` exits 0 without
    /// ever reaching Google — a probe built on it is structurally incapable of seeing the closure.
    /// </summary>
    [Fact]
    public async Task ARetiredRuntime_IsAnsweredWithoutRunningAnything()
    {
        var health = await Probe(Vendor("gemini", "gemini"), exe: FakeCliInvocations.Exe);

        health.Auth.Should().Be("unavailable");
        health.CliFound.Should().BeFalse("nothing was run — the answer is about the vendor, not the binary");
    }

    // ---------- the api arm: a hosted endpoint, a key in the vault, nothing to run ----------

    /// <summary>
    /// An <c>api</c> vendor is probed WITHOUT contacting the endpoint: the key is present, the URL
    /// parses, and the note says the endpoint was not asked — a live <c>GET /models</c> is
    /// <c>--probe-api</c>'s job, and a health probe that spent a paid call on every panel repaint
    /// would be a bill nobody chose. (PLAN_feature_review.md §4.10.)
    /// </summary>
    [Fact]
    public async Task AnApiVendorWithAKeyAndAWellFormedUrl_IsHealthy_WithoutTheEndpointBeingContacted()
    {
        var health = await Probe(Vendor("grok", "api", "https://api.x.ai/v1"), model: "grok-4", hasKey: true);

        health.CliFound.Should().BeTrue("there is no CLI to find — this binary is the shim");
        health.Auth.Should().Be("vault key");
        health.Note.Should().Contain("endpoint not contacted");
    }

    [Fact]
    public async Task AnApiVendorWithoutAKey_IsUnavailable_AndNamesTheVaultEntry()
    {
        var health = await Probe(Vendor("grok", "api", "https://api.x.ai/v1"), model: "grok-4", hasKey: false);

        health.Auth.Should().Be("unavailable");
        health.Note.Should().Contain("'grok'").And.Contain("vault");
    }

    [Theory]
    [InlineData("")]
    [InlineData("api.x.ai/v1")]
    [InlineData("ftp://api.x.ai/v1")]
    public async Task AnApiVendorWhoseUrlIsNotOne_IsUnavailable_AndSaysSo(string baseUrl)
    {
        var health = await Probe(Vendor("grok", "api", baseUrl), model: "grok-4", hasKey: true);

        health.Auth.Should().Be("unavailable");
        health.Note.Should().Contain("URL");
    }

    [Fact]
    public async Task AnApiVendorWithNoModel_IsUnavailable_LikeALocalEngineWithNone()
    {
        var health = await Probe(Vendor("grok", "api", "https://api.x.ai/v1"), hasKey: true);

        health.Auth.Should().Be("unavailable");
        health.Note.Should().Contain("no model");
    }

    // ---------- the local arm: no CLI to ask ----------

    [Fact]
    public async Task ALocalEngineWithAModel_NeedsNoCliAndNoKey()
    {
        var health = await Probe(Vendor("local", "local"), model: "qwen3-coder");

        health.CliFound.Should().BeTrue();
        health.Auth.Should().Be("own auth");
        health.Note.Should().Contain("no CLI, no key, no bill");
    }

    /// <summary>
    /// A local vendor with no model answers 400 to every reviewer, and the probe must say so —
    /// three of nine reviewers in a real code round were lost to exactly this while the health
    /// probe reported the vendor as fine.
    /// </summary>
    [Fact]
    public async Task ALocalEngineWithNoModel_IsUnavailable_AndNamesTheCure()
    {
        var health = await Probe(Vendor("local", "local"));

        health.Auth.Should().Be("unavailable");
        health.Note.Should().Contain("no model").And.Contain("Model");
    }
}
