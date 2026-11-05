using Xunit;

[assembly: AssemblyFixture(typeof(CoaiMcp.Tests.RunTempFixture))]

namespace CoaiMcp.Tests;

/// <summary>
/// The end of the run's temp folder, as an assembly fixture with NO test methods (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, D2).
/// </summary>
/// <remarks>
/// Dedicated, because <see cref="TempDirsAreSwept"/> is both an assembly fixture and a test class: a disposal on it would also run after every one of
/// its test instances. This one is built once and disposed once, after the last test. It does not make the folder — <see cref="RunTemp"/>'s module initializer
/// did, before anything ran — and the process-exit handler calls the same idempotent <see cref="RunTemp.EndOfRun"/>, which is what ends a run that built no fixture.
/// </remarks>
public sealed class RunTempFixture : IDisposable
{
    public void Dispose() => RunTemp.EndOfRun();
}
