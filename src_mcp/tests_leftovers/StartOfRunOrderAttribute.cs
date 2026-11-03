using System.Diagnostics;
using System.Reflection;
using Xunit.v3;

namespace CoaiMcp.Tests;

/// <summary>
/// The proof that the start-of-run sweep ended before ANY test began (E5 S2-B, the re-review's finding e1). It is an ASSEMBLY-level before/after attribute, so xUnit runs its
/// <see cref="Before"/> for every test of every collection of the assembly, on every run; a sweep that had not ended makes THAT test fail with one line, so a future xUnit change that breaks the
/// order is red on the day it lands, on every operating system the suite runs on.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class StartOfRunOrderAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var now = Stopwatch.GetTimestamp();
        var ended = StartOfRunSweep.EndedAt;
        if (ended == 0 || ended > now)
        {
            throw new InvalidOperationException($"coai tests: the start-of-run sweep had not ended when {test.TestDisplayName} started (E5 S2-B, the order proof)");
        }

        StartOfRunSweep.RecordTestStart(now);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
    }
}
