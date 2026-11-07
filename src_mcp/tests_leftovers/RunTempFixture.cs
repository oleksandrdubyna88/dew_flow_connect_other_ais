namespace CoaiMcp.Tests;

/// <summary>
/// The FALLBACK behind the gate (E5 S3), as an assembly fixture with NO test methods. It ends nothing (E5 S6): the sweep fixture's disposal runs the audit and then ends the run folder.
/// </summary>
/// <remarks>
/// <para>Dedicated, because <see cref="TempDirsAreSwept"/> is both an assembly fixture and a test class. This one is built once, before the first test. Each test project registers it with <c>[assembly: AssemblyFixture(typeof(CoaiMcp.Tests.RunTempFixture))]</c> in its <c>RunTempStart.cs</c>.</para>
/// <para><b>The fallback.</b> The gated <c>Main</c> refuses a run before discovery. A host that bypassed it — an IDE's own entry point — still builds this fixture at the first point any test can
/// reach, and it refuses with the SAME one line when <see cref="RunTemp.IsActive"/> is false. A throwing assembly fixture fails every test of the assembly with that message and runs no test body;
/// the exit code is the platform's for failed tests, not 78.</para>
/// <para>It does not make the folder — <see cref="RunTemp.Start"/> did, from the module initializer — and it does not end it. <see cref="RunTemp.EndOfRun"/> runs from the sweep fixture's disposal, right after the end-of-run audit (D9), and from the process-exit handler.</para>
/// </remarks>
public sealed class RunTempFixture
{
    public RunTempFixture()
        : this(static () => RunTemp.IsActive, static () => RunTemp.Refusal(), Environment.ProcessPath ?? "this test executable")
    {
    }

    /// <summary>The seam: the three answers the check needs, so a test can ask it with any.</summary>
    internal RunTempFixture(Func<bool> isActive, Func<string?> refusal, string exe)
    {
        if (!isActive())
        {
            throw new InvalidOperationException(
                refusal() ?? RunTemp.RefusalPrefix + $"the gated entry point did not run (a host started the tests without {exe}'s Main). Run {exe}.");
        }
    }
}
