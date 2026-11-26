using System.Text.Json;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s6, the recorded change: the properties of the coder's unit are written into the job's record, one file for each launch (<c>attempts/&lt;n&gt;/unit-&lt;part&gt;.json</c>) — the unit lives for seconds and an observer that looks for it afterwards finds nothing
/// (the pool-key proof, t5, could not read them). The file holds property NAMES and non-secret values, the network state with its reason, the preflight rows and the submodule mounts; it never holds an Environment value, whatever is in the list it is made from.
/// </summary>
public sealed class ACoderUnitIsRecordedTests
{
    private const string Secret = "sk-ant-oat01-this-text-must-never-be-in-a-record";

    private static BoundaryRecord Record(params string[] properties) =>
        new(BoundaryRecord.UnitBoundary, properties, "host", "the CLI talks to its vendor", [new PreflightRow("worktree-write", true, "a file was written in the worktree")]);

    /// <summary>The systemd-run argument list without one property: the <c>-p</c> before it goes too, or the next argument would be read as its value.</summary>
    private static IReadOnlyList<string> WithoutProperty(IReadOnlyList<string> arguments, string startsWith)
    {
        var list = arguments.ToList();
        var at = list.FindIndex(argument => argument.StartsWith(startsWith, StringComparison.Ordinal));
        if (at > 0)
        {
            list.RemoveRange(at - 1, 2);
        }

        return list;
    }

    [Fact]
    public void TheFile_HoldsEveryPropertyAsANameAndAValue_TheUnitAndTheNetwork_AndTheRows()
    {
        using var folder = TempDir.For("cur-");

        CoderUnitRecord.Write(folder.Path, "c1", "coai-coder-j1-c1", Record("ProtectSystem=strict", "BindReadOnlyPaths=/a:/b", "MemoryMax=2000M"), CoderMounts.None);

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder.Path, "unit-c1.json")));
        var root = json.RootElement;
        root.GetProperty("unit").GetString().Should().Be("coai-coder-j1-c1");
        root.GetProperty("part").GetString().Should().Be("c1");
        root.GetProperty("network").GetString().Should().Be("host");
        root.GetProperty("networkReason").GetString().Should().Contain("vendor");
        root.GetProperty("properties").EnumerateArray().Select(property => (property.GetProperty("name").GetString(), property.GetProperty("value").GetString())).Should()
            .Equal(("ProtectSystem", "strict"), ("BindReadOnlyPaths", "/a:/b"), ("MemoryMax", "2000M"));
        root.GetProperty("preflight").EnumerateArray().Single().GetProperty("item").GetString().Should().Be("worktree-write");
    }

    [Theory]
    [InlineData("Environment")]
    [InlineData("EnvironmentFile")]
    [InlineData("SetCredential")]
    [InlineData("LoadCredential")]
    [InlineData("PassEnvironment")]
    public void AnEnvironmentLikeProperty_IsRecordedByNameOnly_NeverItsValue(string name)
    {
        using var folder = TempDir.For("cur-");

        CoderUnitRecord.Write(folder.Path, "c1", "coai-coder-j1-c1", Record($"{name}=CLAUDE_CODE_OAUTH_TOKEN={Secret}", "Nice=10"), CoderMounts.None);

        var text = File.ReadAllText(Path.Combine(folder.Path, "unit-c1.json"));
        text.Should().NotContain(Secret).And.NotContain("sk-ant");
        using var json = JsonDocument.Parse(text);
        var entry = json.RootElement.GetProperty("properties").EnumerateArray().First();
        entry.GetProperty("name").GetString().Should().Be(name);
        entry.GetProperty("value").GetString().Should().Be("(withheld)");
    }

    [Fact]
    public void TheMounts_AreListedWithTheirPinAndWhetherTheyWereProvided_AndNeverASourcePathOfTheMainCheckout()
    {
        using var folder = TempDir.For("cur-");
        var mounts = new CoderMounts([
            new CoderMount("shared", ".agents/conventions", new string('a', 40), "/job.mounts/m0", string.Empty),
            new CoderMount("other", "deps/other", new string('b', 40), string.Empty, "its git folder is not initialised in the main checkout, so it was not provided"),
        ]);

        CoderUnitRecord.Write(folder.Path, "c2", "coai-coder-j1-c2", Record("Nice=10"), mounts);

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder.Path, "unit-c2.json")));
        var listed = json.RootElement.GetProperty("mounts").EnumerateArray().ToList();
        listed.Select(mount => mount.GetProperty("path").GetString()).Should().Equal(".agents/conventions", "deps/other");
        listed.Select(mount => mount.GetProperty("provided").GetBoolean()).Should().Equal(true, false);
        listed[0].GetProperty("pin").GetString().Should().Be(new string('a', 40));
        listed[1].GetProperty("note").GetString().Should().Contain("not initialised");
    }

    [Fact]
    public async Task ARealCodersLaunch_LeavesItsUnitsPropertiesBehind_WhileTheUnitIsGone()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await CoderBed.CreateAsync();
        var mount = bed.Inner.Mount(".agents/conventions", ("rules/a.md", "rule one\n"));
        using var folder = TempDir.For("cur-");
        var layer = new CoderOsLayer(bed.Job, bed.Turns(), bed.Launcher);

        var outcome = await layer.TurnAsync(bed.Fake("emit", "done"), TestContext.Current.CancellationToken, folder.Path);

        outcome.Should().BeOfType<CoderTurnOutcome.Finished>(outcome.ToString());
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder.Path, "unit-c1.json"), TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("unit").GetString().Should().Be($"coai-coder-{bed.JobId}-c1");
        json.RootElement.GetProperty("started").GetBoolean().Should().BeTrue();
        var properties = json.RootElement.GetProperty("properties").EnumerateArray().Select(property => $"{property.GetProperty("name").GetString()}={property.GetProperty("value").GetString()}").ToList();
        properties.Should().Contain(["ProtectSystem=strict", "ProtectHome=tmpfs", "PrivateNetwork=no", "NoNewPrivileges=yes", $"BindPaths={bed.Worktree}", $"BindReadOnlyPaths={mount.Source}:{mount.Target}"]);
        properties.Should().Equal(outcome.RecordOf()!.Properties, "the file is the list the unit was started with, nothing added or left out");
        json.RootElement.GetProperty("preflight").EnumerateArray().Select(row => row.GetProperty("item").GetString()).Should().Contain(["worktree-write", "submodules-readonly"]);
        (await bed.Inner.ActiveUnitsAsync()).Should().BeEmpty("POSITIVE CONTROL: the unit is gone, so only the record can say what it had");
    }

    [Fact]
    public async Task ALaunchTheBoundaryRefuses_StillLeavesTheRecord_WithTheRowThatSaidNo_AndNoUnitStarted()
    {
        Assert.SkipUnless(SystemdUser.Works(out var why), why);
        await using var bed = await CoderBed.CreateAsync();
        using var folder = TempDir.For("cur-");
        var layer = new CoderOsLayer(bed.Job, bed.Turns(bed.Boundary(arguments => WithoutProperty(arguments, $"BindPaths={bed.Worktree}"))), bed.Launcher);

        var outcome = await layer.TurnAsync(bed.Fake("emit", "done"), TestContext.Current.CancellationToken, folder.Path);

        outcome.Should().BeOfType<CoderTurnOutcome.Refused>(outcome.ToString());
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder.Path, "unit-c1.json"), TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("started").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("preflight").EnumerateArray().Should().Contain(row => !row.GetProperty("held").GetBoolean());
    }
}

internal static class OutcomeRecords
{
    /// <summary>The boundary record of a launch that ran, or null.</summary>
    internal static BoundaryRecord? RecordOf(this CoderTurnOutcome outcome) => outcome switch
    {
        CoderTurnOutcome.Finished finished => finished.Run.Record,
        CoderTurnOutcome.Failed failed => failed.Run.Record,
        CoderTurnOutcome.WriteStopped stopped => stopped.Run.Record,
        CoderTurnOutcome.Refused refused => refused.Record,
        _ => null,
    };
}
