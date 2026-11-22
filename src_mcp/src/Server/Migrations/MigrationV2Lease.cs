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

    /// <summary>The lease, or why not: <see cref="LeaseTake.Busy"/> when another migration holds it, <see cref="LeaseTake.Unusable"/> when the folder cannot take it.</summary>
    public static LeaseTake Take(string dataDir)
    {
        var file = FileFor(dataDir);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new LeaseTake.Unusable(e.Message);
        }

        return HeldFile.Take(file) switch
        {
            Hold.Taken taken => new LeaseTake.Taken(new MigrationV2Lease(taken.Stream)),
            Hold.Busy => new LeaseTake.Busy(BusySentence),
            Hold.Unusable unusable => new LeaseTake.Unusable(unusable.Cause.Message),
            _ => new LeaseTake.Unusable("the lease could not be taken"),
        };
    }

    /// <summary>What a run says when another migration holds the lease.</summary>
    public const string BusySentence = "another migration holds the lease";

    public void Dispose() => _held.Dispose();
}

/// <summary>The answer to taking the migration's lease: a closed set of three cases (the C# doctrine §5), so no caller reads a flag next to a nullable.</summary>
internal abstract record LeaseTake
{
    private LeaseTake()
    {
    }

    /// <summary>The lease is held by the caller, who disposes it.</summary>
    public sealed record Taken(MigrationV2Lease Lease) : LeaseTake;

    /// <summary>Another migration holds it; the caller runs nothing and says so.</summary>
    public sealed record Busy(string Why) : LeaseTake;

    /// <summary>The folder cannot take the lease file at all.</summary>
    public sealed record Unusable(string Why) : LeaseTake;
}
