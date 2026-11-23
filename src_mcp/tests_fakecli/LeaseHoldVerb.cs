using CoaiMcp.Runners.Files;

namespace CoaiMcp.FakeCli;

/// <summary>
/// A role job's lease held by a CHILD process (epoch 3 E1 s1): the product's own <c>HeldFile.TryHold</c> on the file named, kept for the time given. The kernel releases the hold when this
/// process dies, which is exactly the fact the tests of a dead runner and a stopped runner stand on — so they kill it, and stop it, as real processes.
/// <c>lease-hold &lt;file&gt; &lt;ms&gt;</c> writes its pid beside the file (<c>&lt;file&gt;.held</c>) once it holds it, so a test knows when to go on; exits 75 when somebody else already holds it.
/// </summary>
internal static class LeaseHoldVerb
{
    internal static int Run(string file, int milliseconds)
    {
        if (HeldFile.TryHold(file) is not { } held)
        {
            return 75;
        }

        using (held)
        {
            File.WriteAllText(file + ".held", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Thread.Sleep(milliseconds);
        }

        return 0;
    }
}
