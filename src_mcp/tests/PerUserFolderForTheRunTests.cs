using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Epoch 3 E4 s5 t6 d2: the run makes its own per-user data folder only where the machine gives none (a check unit), and never touches the machine's own.</summary>
public sealed class PerUserFolderForTheRunTests
{
    [Fact]
    public void WhereTheMachineGivesNoFolder_TheRunMakesOneInItsOwnFolder_AndPointsTheVariableAtIt()
    {
        using var run = TempDir.For("peruser-");
        var set = new Dictionary<string, string?>();

        var made = PerUserFolderForTheRun.Ensure(run.Path, windows: false, () => string.Empty, (name, value) => set[name] = value);

        made.Should().Be(Path.Combine(run.Path, "data"));
        Directory.Exists(made).Should().BeTrue();
        set.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string?>("XDG_DATA_HOME", made));
    }

    [Fact]
    public void WhereTheMachineHasTheFolder_NothingIsMade_AndNothingIsSet()
    {
        using var run = TempDir.For("peruser-");
        var set = new Dictionary<string, string?>();

        PerUserFolderForTheRun.Ensure(run.Path, windows: false, () => "/home/x/.local/share", (name, value) => set[name] = value).Should().BeNull();

        set.Should().BeEmpty();
        Directory.Exists(Path.Combine(run.Path, "data")).Should().BeFalse();
    }

    [Fact]
    public void OnWindows_OrWithNoRunFolder_NothingIsMade()
    {
        var set = new Dictionary<string, string?>();

        PerUserFolderForTheRun.Ensure("/nowhere", windows: true, () => string.Empty, (name, value) => set[name] = value).Should().BeNull();
        PerUserFolderForTheRun.Ensure(string.Empty, windows: false, () => string.Empty, (name, value) => set[name] = value).Should().BeNull();

        set.Should().BeEmpty();
    }

    [Fact]
    public void ThisRun_HasAPerUserFolder_WhateverTheMachineGave()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows always has an application-data folder");

        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Should().NotBeEmpty("the product reads it for its data folder, its coder worktrees and the agy launch folder; the run makes its own where the machine has none");
    }
}
