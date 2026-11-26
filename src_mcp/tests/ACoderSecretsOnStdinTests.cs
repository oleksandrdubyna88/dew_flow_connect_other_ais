using System.Text;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7, the third security judgement: ONE road for every secret that crosses into the coder's unit — the first lines of the unit's stdin, which its own shell reads and exports before the exec. A pool key, a role row's own
/// <c>ANTHROPIC_API_KEY</c> and any other explicit variable of the launch go that way; none is a <c>--setenv</c> (which would put the value in the unit's <c>Environment</c> property and in a 0644 transient unit file for as long as the unit lives), none is
/// left in the environment of the <c>systemd-run</c> client, and the prompt behind them arrives whole. Real <c>systemd-run --user</c> units on temp folders; a made-up canary in every secret; skipped, with the reason, where there is no user manager.
/// </summary>
public sealed class ACoderSecretsOnStdinTests
{
    private const string PoolKeyName = "CLAUDE_CODE_OAUTH_TOKEN";

    private static void NeedAUserManager() => Assert.SkipUnless(SystemdUser.Works(out var why), why);

    /// <summary>A made-up secret that carries the canary word, built at run time so this source holds no token-shaped literal.</summary>
    private static string Secret(string tag) => string.Concat("made-up-", KeyWorld.Sentinel, "-", tag, "-", Guid.NewGuid().ToString("N"));

    // ---------- the rest of stdin is intact ----------

    public static TheoryData<string> Prompts => new()
    {
        "Fix the thing.\nSecond line — with a dash.\n",
        "\n\nstarts with blank lines\r\nand a CRLF line\r\nand no final newline",
        "x",
        "a line that looks like a key: sk-ant-oat01-not-the-key\nand a second one\n",
    };

    [Theory]
    [MemberData(nameof(Prompts))]
    public async Task ThePrompt_ArrivesWholeAfterTheKeyLine_OneLineOrMany(string prompt)
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var request = bed.Fake("echo-stdin") with
        {
            StdIn = prompt,
            Environment = new Dictionary<string, string?> { [PoolKeyName] = Secret("pool") },
        };

        var run = await bed.RunLaunchAsync(request);

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        run.Result.StdOut.Should().Be(prompt, "the unit's shell took the key line and nothing else: every byte of the prompt reached the program, and the key is not in it");
    }

    // ---------- every secret travels on stdin ----------

    [Fact]
    public async Task ARowsOwnKey_AnOtherSecret_AndThePoolKey_AllTravelOnStdin_TheChildHasThemAll_NoUnitPropertyFileProcessOrClientHoldsThem()
    {
        NeedAUserManager();
        await using var bed = await CoderBed.CreateAsync();
        var rowKey = Secret("row");
        var other = Secret("other");
        var pool = Secret("pool");
        var request = bed.Shell("/usr/bin/env; /bin/sleep 4") with
        {
            Environment = new Dictionary<string, string?> { ["ANTHROPIC_API_KEY"] = rowKey, ["OTHER_VENDOR_TOKEN"] = other, [PoolKeyName] = pool },
            Passthrough = new HashSet<string>(),
        };
        var unit = $"coai-coder-{bed.JobId}-c1";
        var running = bed.RunLaunchAsync(request);
        var seen = new StringBuilder();
        var unitText = new StringBuilder();
        var clientEnvironment = new StringBuilder();
        var properties = 0;
        var clients = 0;
        for (var until = DateTime.UtcNow.AddSeconds(30); !running.IsCompleted && DateTime.UtcNow < until;)
        {
            var shown = UnitLeakWatch.UnitProperties($"{unit}.service");
            properties += shown.Contains("Environment=", StringComparison.Ordinal) ? 1 : 0;
            unitText.AppendLine(shown).AppendLine(UnitLeakWatch.TransientFiles(bed.JobId));
            seen.Append(UnitLeakWatch.Cmdlines());
            clientEnvironment.Append(UnitLeakWatch.ClientEnvironments(unit));
            clients += UnitLeakWatch.Clients(unit);
            await Task.Delay(200, TestContext.Current.CancellationToken);
        }

        var run = await running;

        run.Result.ExitCode.Should().Be(0, run.Result.StdErr);
        var lines = run.Result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The PWD is the one this test's own `sh -c` sets for itself after the unit's shell unset it; it is no variable of the launch.
        lines.Select(line => line.Split('=')[0]).Where(name => name != "PWD").Should().BeEquivalentTo(["PATH", "HOME", "TMPDIR", "LANG", "TERM", "NO_COLOR", "GIT_TERMINAL_PROMPT", "ANTHROPIC_API_KEY", "OTHER_VENDOR_TOKEN", PoolKeyName], "the allow-list and the three secrets, nothing else");
        lines.Should().Contain([$"ANTHROPIC_API_KEY={rowKey}", $"OTHER_VENDOR_TOKEN={other}", $"{PoolKeyName}={pool}"], "the child has every secret whole, each under its own name");

        properties.Should().BeGreaterThan(0, "the property of the live unit was read at least once, so the check can see a unit");
        clients.Should().BeGreaterThan(0, "the systemd-run client of the unit was found while it ran");
        seen.Length.Should().BeGreaterThan(1000, "the scan read real command lines");
        unitText.ToString().Should().NotContain(KeyWorld.Sentinel, "neither `systemctl --user show -p Environment` nor the 0644 transient unit file ever held a secret");
        seen.ToString().Should().NotContain(KeyWorld.Sentinel, "no command line of the machine held a secret");
        clientEnvironment.ToString().Should().NotContain(KeyWorld.Sentinel, "no secret is in the environment of the systemd-run client");

        var started = bed.Launcher.Requests.Single(item => item.Arguments.Contains(unit));
        started.Arguments.Where(argument => argument.StartsWith("--setenv", StringComparison.Ordinal)).Should().NotContain(argument => argument.Contains("ANTHROPIC") || argument.Contains("OTHER_VENDOR") || argument.Contains(PoolKeyName), "a secret is not a unit property");
        started.Arguments.Should().NotContain(argument => argument.Contains(KeyWorld.Sentinel, StringComparison.Ordinal));
        started.Environment.Values.Should().NotContain(value => value != null && value.Contains(KeyWorld.Sentinel, StringComparison.Ordinal), "the request that started the client holds no secret");
    }
}
