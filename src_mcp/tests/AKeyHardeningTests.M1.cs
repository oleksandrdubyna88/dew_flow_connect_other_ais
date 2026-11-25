using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The part of the hardening tests (the security judgement of s7 part 1) that covers: M1.</summary>
public sealed partial class AKeyHardeningTests
{
    // ---------- must 1: no COAI_CLAUDE_* name reaches a child ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task M1_ACoaiClaudeName_NeverReachesARealChild_WhateverTheLaunchInherits(bool inherits)
    {
        var names = new[] { "COAI_CLAUDE_KEY_one", "COAI_CLAUDE_ACCOUNT_one", "COAI_CLAUDE_KEYS_FILE" };
        var environment = names.ToDictionary(name => name, _ => (string?)KeyWorld.Token("env"));
        environment["COAI_HARDENING_COMPANION"] = "1";
        var request = new ProcessRequest(RoleJobWorld.FakeCli, ["env-names"], AppContext.BaseDirectory)
        {
            Environment = environment,
            InheritsEnvironment = inherits,
            Passthrough = new HashSet<string>(ProcessEnvironment.Passthrough.Concat(names).Concat(["COAI_HARDENING_COMPANION"])),
        };

        var run = await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken);

        run.ExitCode.Should().Be(0);
        var seen = run.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).ToList();
        seen.Should().Contain("COAI_HARDENING_COMPANION", "the companion: the launcher does pass an ordinary name, so the check can see a name");
        seen.Should().NotContain(names, "a pool key's name never crosses to a child — a gate reviewer, a consultation, git, the role runner");
    }
}
