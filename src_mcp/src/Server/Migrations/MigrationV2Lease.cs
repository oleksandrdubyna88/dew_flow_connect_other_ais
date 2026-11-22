using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server.Migrations;

/// <summary>
/// <c>claims/migrate-v2.lease</c>: the one migration running on a data folder — a file held open exclusively, which the kernel releases when the
/// holder is killed (the shape <c>SessionClaim</c> and <c>SessionTurn</c> already use, so there is no stale lease to break).
/// </summary>
/// <remarks>Only an apply takes it; a dry run reads and creates nothing.</remarks>
internal sealed class MigrationV2Lease : IDisposable
{
    private readonly FileStream _held;

    private MigrationV2Lease(FileStream held) => _held = held;

    public static string FileFor(string dataDir) => Path.Combine(dataDir, "claims", "migrate-v2.lease");

    /// <summary>The lease, or why not: <see cref="Busy"/> when another migration holds it, <see cref="Unusable"/> when the folder cannot take it.</summary>
    public static (MigrationV2Lease? Lease, bool Busy, string Why) Take(string dataDir)
    {
        var file = FileFor(dataDir);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, false, e.Message);
        }

        return HeldFile.Take(file) switch
        {
            Hold.Taken taken => (new MigrationV2Lease(taken.Stream), false, string.Empty),
            Hold.Busy => (null, true, "another migration holds the lease"),
            Hold.Unusable unusable => (null, false, unusable.Cause.Message),
            _ => (null, false, "the lease could not be taken"),
        };
    }

    public void Dispose() => _held.Dispose();
}
