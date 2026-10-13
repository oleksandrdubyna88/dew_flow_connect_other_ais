namespace CoaiMcp.Runners.Snapshots;

/// <summary>
/// How the DESTINATION filesystem compares names — asked of the filesystem, never assumed from the operating system.
/// </summary>
/// <remarks>
/// The exclusions and the collision rule are "matched with the destination filesystem's case rules" (D6). Windows folds
/// case, Linux does not, and macOS does by default but not always — and a probe answers all three the same way: write
/// a name in lower case, ask for it in upper case. It costs one file in a directory that is about to be filled anyway.
/// </remarks>
internal static class DestinationCase
{
    private const string Lower = "case-probe";
    private const string Upper = "CASE-PROBE";

    /// <summary>The comparison names under <paramref name="directory"/> are matched with.</summary>
    public static StringComparison Probe(string directory)
    {
        var lower = Path.Combine(directory, Lower);
        File.WriteAllBytes(lower, []);
        try
        {
            return File.Exists(Path.Combine(directory, Upper)) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }
        finally
        {
            File.Delete(lower);
        }
    }
}
