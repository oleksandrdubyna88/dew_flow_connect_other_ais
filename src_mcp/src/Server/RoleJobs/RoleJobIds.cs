using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// A job id: the folder name under <c>roles/jobs/</c>, the systemd unit's suffix and the argument of <c>--role-run</c>. One strict shape, so an id that arrives from a
/// caller or from argv can never name a folder outside the jobs root (<c>../x</c>, a separator, an absolute path) or a unit with a character systemd would reject.
/// </summary>
internal static partial class RoleJobIds
{
    /// <summary>The prefix of every unit a runner is started as.</summary>
    public const string UnitPrefix = "coai-role-";

    [GeneratedRegex(@"^r\d{14}-[0-9a-f]{8}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Shape();

    /// <summary>A fresh id: the UTC second, then eight random hex digits. Sorts by time, so a listing of the folder reads oldest first.</summary>
    public static string New(TimeProvider clock) =>
        $"r{clock.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}-{Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4))}";

    /// <summary>True only for the exact shape; nothing is trimmed or folded.</summary>
    public static bool IsValid(string? id) => id is { Length: 24 } && Shape().IsMatch(id);

    /// <summary>The unit a job's runner runs as (<c>coai-role-&lt;jobId&gt;</c>).</summary>
    public static string UnitOf(string jobId) => UnitPrefix + jobId;
}
