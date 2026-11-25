using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The start sync against THIS process's own environment (the second security judgement): the default unset of <see cref="KeyStartup"/> is the real one. In the serial collection of the tests that read or set <c>COAI_CLAUDE_KEY_*</c> in the process.
/// </summary>
[Collection("key-process-environment")]
public sealed class AKeyStartupEnvironmentTests
{
    [Fact]
    public void TheRealUnsetOfTheStartSync_RemovesTheVariablesFromThisProcess()
    {
        using var world = new KeyWorld();
        const string Name = "COAI_CLAUDE_KEY_secondjudgement";
        Environment.SetEnvironmentVariable(Name, KeyWorld.Token("a"));
        try
        {
            KeyStartup.Sync(world.DataDir, _ => null, Serilog.Core.Logger.None);

            Environment.GetEnvironmentVariable(Name).Should().BeNull("the default unset is the real one: a no-op would leave the key in the environment of every child that inherits it");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Name, null);
        }
    }
}
