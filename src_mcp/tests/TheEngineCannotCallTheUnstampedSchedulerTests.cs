using System.Reflection;
using System.Runtime.CompilerServices;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>BoundedScheduler.RunAllAsync</c> returns the outcomes without the moment the scheduler returned; the round engine must read that moment
/// from the scheduler itself (<c>RunAllStampedAsync</c>), never from a clock read after the await — otherwise a cancel that fires after the
/// scheduler returned is read as having ended a turn (§10.3 of todo/PLAN_a_cancelled_review_stops_its_round_S2_spec.md). The wrapper stays for
/// the tests that need only the outcomes; production code must not be able to reach it by mistake, so it is not visible to it.
/// </summary>
public sealed class TheEngineCannotCallTheUnstampedSchedulerTests
{
    [Fact]
    public void TheUnstampedWrapper_IsNotPartOfTheSchedulersPublicSurface()
    {
        var wrapper = typeof(BoundedScheduler).GetMethod(nameof(BoundedScheduler.RunAllStampedAsync))!;
        var unstamped = typeof(BoundedScheduler).GetMethod("RunAllAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        wrapper.IsPublic.Should().BeTrue("the engine calls the stamped one");
        unstamped.IsPublic.Should().BeFalse("the wrapper discards the return stamp: only the tests, which read outcomes alone, may call it");
    }

    [Fact]
    public void TheServerAssembly_IsNotGrantedTheRunnersInternals()
    {
        var granted = typeof(BoundedScheduler).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();

        typeof(RoundEngine).Assembly.GetName().Name.Should().NotBeNull();
        granted.Should().NotContain(typeof(RoundEngine).Assembly.GetName().Name!,
            "an internal wrapper is only out of the engine's reach while the server assembly cannot see the runners' internals");
    }
}
