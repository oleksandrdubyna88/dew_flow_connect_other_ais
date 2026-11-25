using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.Keys;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The part of the hardening tests (the security judgement of s7 part 1) that covers: M3.</summary>
public sealed partial class AKeyHardeningTests
{
    // ---------- must 3: pool.lock is a regular file, never a link ----------

    [Fact]
    public void M3_APoolLockThatIsALink_IsRefused_AndItsTargetIsNeitherOpenedNorChanged()
    {
        using var world = new KeyWorld();
        world.AddKey("A", "a");
        var target = Path.Combine(world.DataDir, "elsewhere.txt");
        File.WriteAllText(target, "private\n");
        File.SetUnixFileMode(target, Private);
        File.Delete(Path.Combine(world.KeysRoot, "pool.lock"));
        File.CreateSymbolicLink(Path.Combine(world.KeysRoot, "pool.lock"), target);

        var listed = world.Admin.List(false);

        listed.Ok.Should().BeFalse("a lock that is a link is refused");
        listed.Refusal.Should().Contain("pool.lock");
        File.GetUnixFileMode(target).Should().Be(Private);
    }

    [Fact]
    public void M3_ADanglingPoolLock_CreatesNothingAtItsTarget()
    {
        using var world = new KeyWorld();
        world.AddKey("A", "a");
        var target = Path.Combine(world.DataDir, "not-made.txt");
        File.Delete(Path.Combine(world.KeysRoot, "pool.lock"));
        File.CreateSymbolicLink(Path.Combine(world.KeysRoot, "pool.lock"), target);

        world.Admin.List(false).Ok.Should().BeFalse();

        File.Exists(target).Should().BeFalse("the open must not follow a link and make a file where it points");
    }
}
