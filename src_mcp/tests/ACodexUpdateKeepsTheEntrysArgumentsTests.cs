using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E3 plan gate, finding 2: codex's <c>mcp add</c> REPLACES the entry, so an update that does not hand the old <c>args</c> over loses them. Verified on codex-cli 0.171.0 (scratch HOME): <c>codex mcp add [--env K=V] coai -- &lt;path&gt; &lt;args…&gt;</c>
/// stores every argument after the path as <c>args</c>, in order — including an empty one, a quote and one that looks like a flag. An argument the reader cannot hand over as text is a refusal, never a quiet drop.
/// </summary>
public sealed class ACodexUpdateKeepsTheEntrysArgumentsTests
{
    [Fact]
    public async Task AnUpdateWithArgsAndEnv_KeepsBoth_InOrder()
    {
        using var world = InstallWorld.Create();
        world.Put(world.CodexToml, """
            [mcp_servers.coai]
            command = "/old/coai/versions/1.0.0/coai-mcp"
            args = ["--x", "", "a b", 'q"uote', "--env=Y"]

            [mcp_servers.coai.env]
            COAI_DATA_DIR = "/data/coai"
            COAI_DATA_SIDE = "wsl"
            """);

        var (code, _, err) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(0, err);
        InstallOutput.SameToml(
            world.CodexTomlText,
            $"[mcp_servers.coai]\ncommand = \"{world.BinaryToml}\"\nargs = [\"--x\", \"\", \"a b\", 'q\"uote', \"--env=Y\"]\n[mcp_servers.coai.env]\nCOAI_DATA_DIR = \"/data/coai\"\nCOAI_DATA_SIDE = \"wsl\"\n").Should().BeTrue(world.CodexTomlText);
        world.Calls.Single(line => line.StartsWith("codex mcp add")).Should().Contain($"coai -- {world.Binary} --x  a b q\"uote --env=Y |", "the arguments follow the path after the `--`, as codex takes them");
    }

    [Fact]
    public async Task AnArgumentThatIsNotText_IsRefused_NotDropped()
    {
        using var world = InstallWorld.Create();
        const string old = """
            [mcp_servers.coai]
            command = "/old/coai/versions/1.0.0/coai-mcp"
            args = ["--x", 5]
            """;
        world.Put(world.CodexToml, old);

        var (code, output, _) = await world.RunAsync("--binary", world.Binary);

        code.Should().Be(78);
        output.Result("codex").Should().Be("refused");
        output.Client("codex").GetProperty("reason").GetString().Should().Contain("args");
        world.Calls.Should().NotContain(line => line.StartsWith("codex mcp add"));
        world.CodexTomlText.Should().Be(old);
    }
}
