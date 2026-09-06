using System.Diagnostics;
using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The exclusion that keeps two launches off one account, and the state that survives a restart.
/// </summary>
public sealed class SlotRegistryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-slots-" + Guid.NewGuid().ToString("N"));

    public SlotRegistryTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SlotRegistry New() => new(_dir, new JsonFileStore());

    [Fact]
    public async Task AHeldAccountIsRefusedAndFreeOnceReleased()
    {
        var registry = New();
        var slot = registry.Read("codex", "a");

        var first = await registry.AcquireAsync(slot, TimeSpan.Zero);
        first.Should().NotBeNull();

        (await registry.AcquireAsync(slot, TimeSpan.Zero)).Should().BeNull("it is in use");

        first!.Dispose();
        var again = await registry.AcquireAsync(slot, TimeSpan.Zero);
        again.Should().NotBeNull("releasing it makes it available");
        again!.Dispose();
    }

    [Fact]
    public async Task ReleasingTwiceIsNotAnError()
    {
        var registry = New();
        var lease = await registry.AcquireAsync(registry.Read("codex", "a"), TimeSpan.Zero);

        lease!.Dispose();
        var second = () => lease.Dispose();

        second.Should().NotThrow("a using-block plus an explicit release is ordinary code");
    }

    [Fact]
    public async Task ANeverUsedAccountReportsThatItNeedsSigningIn()
    {
        var slot = New().Read("codex", "brand-new");

        slot.NeedsSignIn.Should().BeTrue(
            "an account nobody has signed in is not 'ready' — the catalog must say what to do next");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ACooldownSurvivesARestart()
    {
        var registry = New();
        var slot = registry.Read("codex", "a");
        var now = DateTimeOffset.UtcNow;

        registry.MarkCoolingDown(slot, "You've hit your session limit · resets 9:30pm (UTC)", now);

        // A NEW registry over the same directory is what a restarted container has. Without the
        // persisted state it would immediately pick the exhausted account again.
        var afterRestart = New().Read("codex", "a");
        afterRestart.CooldownUntilUtc.Should().NotBeNull();
        afterRestart.IsReady(now).Should().BeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RepeatedRateLimitsBackOffFurtherEachTime()
    {
        var registry = New();
        var now = DateTimeOffset.UtcNow;

        registry.MarkCoolingDown(registry.Read("codex", "a"), "rate limited", now);
        var first = registry.Read("codex", "a");

        registry.MarkCoolingDown(first, "rate limited", now);
        var second = registry.Read("codex", "a");

        (second.CooldownUntilUtc!.Value - now).Should().BeGreaterThan(first.CooldownUntilUtc!.Value - now,
            "the same thirty minutes repeated is not a back-off");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ASuccessClearsTheCooldownAndItsCounter()
    {
        var registry = New();
        var now = DateTimeOffset.UtcNow;
        registry.MarkCoolingDown(registry.Read("codex", "a"), "rate limited", now);

        registry.MarkSucceeded(registry.Read("codex", "a"));

        var slot = registry.Read("codex", "a");
        slot.CooldownUntilUtc.Should().BeNull();
        slot.ConsecutiveCooldowns.Should().Be(0,
            "otherwise an account limited three times last week starts its next back-off at four hours");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task OnlyASignInClearsNeedsSignIn()
    {
        var registry = New();
        var slot = registry.Read("codex", "a");
        registry.MarkNeedsSignIn(slot, "invalid_grant");

        registry.Read("codex", "a").NeedsSignIn.Should().BeTrue();
        // Waiting is what a cooldown responds to. This one does not: no amount of time signs an
        // account back in, and treating it as temporary is how a slot gets picked for every job and
        // fails every one of them.
        registry.Read("codex", "a").IsReady(DateTimeOffset.UtcNow.AddDays(30)).Should().BeFalse();

        registry.MarkSignedIn(registry.Read("codex", "a"));
        registry.Read("codex", "a").NeedsSignIn.Should().BeFalse();
        await Task.CompletedTask;
    }

    /// <summary>
    /// The whole reason the lock is a file: the other holder is a DIFFERENT PROCESS.
    /// </summary>
    /// <remarks>
    /// <c>login</c> arrives as <c>docker compose exec coai-server login codex a</c> — an in-memory
    /// flag cannot see it, and an in-process test of the lock would prove the wrong thing entirely.
    /// So this holds the account here and runs the real <c>coai-server login</c> binary, asserting it
    /// refuses with its own busy exit code rather than proceeding to rewrite the credentials.
    /// </remarks>
    [Fact]
    public async Task ASecondPROCESSIsRefusedTheSameAccount()
    {
        File.WriteAllText(
            Path.Combine(_dir, "vendors.json"),
            """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");

        var registry = New();
        using var held = await registry.AcquireAsync(registry.Read("codex", "a"), TimeSpan.Zero);
        held.Should().NotBeNull();

        var (exitCode, output) = await RunLoginAsync("codex", "a");

        exitCode.Should().Be(VendorLogin.BusyExitCode,
            "a sign-in while a review holds the account is the two-writers race that produces invalid_grant");
        output.Should().Contain("busy");
    }

    [Fact]
    public async Task ASecondProcessProceedsOnceTheAccountIsFree()
    {
        File.WriteAllText(
            Path.Combine(_dir, "vendors.json"),
            """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");

        var registry = New();
        var lease = await registry.AcquireAsync(registry.Read("codex", "a"), TimeSpan.Zero);
        lease!.Dispose();

        var (exitCode, _) = await RunLoginAsync("codex", "a");

        // It gets PAST the lock — and then fails, because there is no codex CLI on a test runner.
        // That is the point: the refusal above is the lock, not the missing binary.
        exitCode.Should().NotBe(VendorLogin.BusyExitCode);
    }

    /// <summary>Runs the real server executable as <c>login</c>, the way `docker compose exec` does.</summary>
    private async Task<(int ExitCode, string Output)> RunLoginAsync(string vendor, string slot)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "coai-server" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        File.Exists(exe).Should().BeTrue($"the test needs the server binary beside it, at {exe}");

        var start = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("login");
        start.ArgumentList.Add(vendor);
        start.ArgumentList.Add(slot);
        start.Environment["Coai__DataDir"] = _dir;
        // Otherwise the busy case waits the two-minute default and this test takes two minutes.
        start.Environment["Coai__LoginWaitSeconds"] = "1";
        // And a short sign-in timeout, so the free-slot case does not sit on a real device flow.
        start.Environment["Coai__LoginTimeoutSeconds"] = "2";

        using var process = Process.Start(start)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + stderr);
    }
}
