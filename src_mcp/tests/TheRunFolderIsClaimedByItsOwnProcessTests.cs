using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The run folder belongs to ONE process, so its claim names that process — never a record the suite inherited in <c>TESTSTODELETE_RUN</c> (E5 S2-B, Opus review 1).
/// <see cref="Owner.RunRecord"/> hands a helper's claim the inherited record when it names a live run of the caller, so that what a CHILD makes belongs to the run. For the
/// run folder itself that is wrong: a wrapper that sets the variable and dies while the suite runs would leave the suite's folder claimed by a dead pid, and another sweep
/// would remove the running suite's temp folder after ten minutes.
/// </summary>
public sealed class TheRunFolderIsClaimedByItsOwnProcessTests : IDisposable
{
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(60);

    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    [Fact]
    public void AnInheritedRunRecord_NeverBecomesTheClaimOfTheRunFolder_AndTheRunFolderOutlivesItsWrapper()
    {
        var root = _owned.Dir("s2b-wrapper");
        var wrapper = Owner.This; // stands for the wrapper: LIVE now, so the child's RunRecord accepts it
        var env = RunTempFolder.Variables.ToDictionary(name => name, _ => (string?)root);
        env["TESTSTODELETE_RUN"] = wrapper.Format();
        env[RunTempChildren.KilledVariable] = "1";

        var result = SuiteChild.Run(["--filter-class", "*KilledRunChild"], env, ChildTimeout);

        result.TimedOut.Should().BeFalse(result.StdErr);
        var claims = Directory.GetFiles(root, "*.owner");
        claims.Should().ContainSingle("a killed run leaves exactly its folder and its claim");
        var claim = claims.Single();
        var owner = Owner.Parse(File.ReadAllText(claim));
        owner.Should().NotBeNull();
        owner!.Pid.Should().Be(result.Pid, "the claim names the process that owns the folder, not the wrapper whose record it inherited");
        owner.Pid.Should().NotBe(wrapper.Pid);

        // The wrapper dies (a probe that calls it dead) while the suite is still running (a probe that calls the child live): the sweep, ten minutes on, keeps the folder.
        var later = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(11);
        Probe Probe(long pid) => pid == wrapper.Pid ? new Probe(ProbeState.Dead, null) : new Probe(ProbeState.Live, owner.Start);
        var sweep = LeftoversSweep.Run(new SweepOptions(root, later, LeftoversSweep.MinAge, Probe, _ => true));

        sweep.Removed.Should().BeEmpty("the running suite's temp folder is not a dead wrapper's");
        sweep.Preserved.Should().Contain(p => p.Reason == "live");
        Directory.GetDirectories(root).Should().ContainSingle();
    }
}
