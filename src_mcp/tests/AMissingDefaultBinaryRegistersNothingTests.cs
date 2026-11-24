using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3 plan gate, finding 3: without <c>--binary</c> the installer registers its own stable path. When that file is not there, it must not register a missing executable. Every target answers
/// <c>refused</c> with one sentence, before any lock or backup, exit 78: the arguments are fine (the person named nothing wrong) — it is a fact about this machine, like a read-only file, and
/// 78 is the code those refusals already carry. A <c>--binary</c> that is missing stays 65, because there the person named it.
/// </summary>
public sealed class AMissingDefaultBinaryRegistersNothingTests
{
    private static string Missing(InstallWorld world) => Path.Combine(world.Root, "opt", "gone", OperatingSystem.IsWindows() ? "coai-mcp.exe" : "coai-mcp");

    [Fact]
    public async Task EveryTargetIsRefused_WithTheSentence_Exit78_AndNothingIsRunOrWritten()
    {
        using var world = InstallWorld.Create();
        var before = world.HomeHash();
        var missing = Missing(world);

        var (code, output, _) = await world.RunAsync(world.Deps(self: missing), "setup-clients");

        code.Should().Be(78);
        foreach (var cli in new[] { "claude", "codex" })
        {
            output.Result(cli).Should().Be("refused");
            output.Client(cli).GetProperty("targets").EnumerateArray().Select(target => target.GetProperty("action").GetString()).Should().OnlyContain(action => action == "refuse");
            output.Client(cli).GetProperty("reason").GetString().Should().Be($"no coai-mcp at {missing}; pass --binary or install the extension first");
        }

        world.Calls.Should().BeEmpty("no vendor command ran");
        world.HomeHash().Should().Be(before, "no lock, no backup, no entry");
        world.BackupFolders.Should().BeEmpty();
    }

    [Fact]
    public async Task ADryRun_AnswersTheSame()
    {
        using var world = InstallWorld.Create();

        var (code, output, _) = await world.RunAsync(world.Deps(self: Missing(world)), "setup-clients", "--dry-run");

        code.Should().Be(78);
        output.Result("claude").Should().Be("refused");
    }

    [Fact]
    public async Task ANamedBinary_StillRegisters_WhateverThisBinarysOwnPathIs()
    {
        using var world = InstallWorld.Create();

        var (code, _, err) = await world.RunAsync(world.Deps(self: Missing(world)), "setup-clients", "--binary", world.Binary);

        code.Should().Be(0, err);
        InstallOutput.ClaudeEntry(world.ClaudeJsonText)!["command"]!.GetValue<string>().Should().Be(world.Binary);
    }

    [Fact]
    public async Task ANamedBinaryThatIsMissing_IsStillAnArgumentError_65()
    {
        using var world = InstallWorld.Create();

        var (code, _, err) = await world.RunAsync("--binary", Missing(world));

        code.Should().Be(65);
        err.Should().Contain("is not an executable file");
        world.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ARemoveNeedsNoBinary_SoItIsNotRefusedForOne()
    {
        using var world = InstallWorld.Create();
        InstallOutput.WithOldEntries(world);

        var (code, _, err) = await world.RunAsync(world.Deps(self: Missing(world)), "setup-clients", "--remove");

        code.Should().Be(0, err);
    }
}
