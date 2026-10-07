using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Server;

/// <summary>
/// Whether THIS start takes the consultants survey, or one already taken stands for it — one survey per window per
/// build, side and effective settings, however many servers start (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, D2).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Every stdio-server start probed four CLIs for <c>consultants.json</c>. Seven editor sessions
/// restarting together probed seven times, on a machine the restart storm had already saturated (2026-10-06).</para>
/// <para><b>The atomic operation</b> is <see cref="FileMode.CreateNew"/> of
/// <c>consultations/health/consultants.survey.&lt;hash&gt;.claim</c>, and only the create decides. The NAME carries the
/// window slot and the identity (<see cref="IdentityOf"/>), so nothing is ever taken over: a new slot, an upgrade, a
/// settings change or another client environment is a new name, and starts racing for one name are decided by the
/// create alone. Taking over a stale claim was the first design and could not be fenced (plan round 1, codex).</para>
/// <para><b>Residual</b>, stated where a caller reads it: two starts on either side of a slot boundary both survey —
/// at most two, never seven. A claim is held for its window and not renewed: it is a "taken" mark, not a lock on work
/// in progress, so a survey still running when its slot ends can be joined by one more. A survey that fails or is
/// cancelled releases its claim (fenced by the claim's own id) so the next start in the slot surveys; one whose
/// process was killed first leaves it until the slot ends.</para>
/// <para><b>Growth.</b> Each take deletes claims older than two windows, so at rest the directory holds about one per
/// identity in use.</para>
/// </remarks>
internal sealed class ConsultantsSurveyClaim
{
    private const string Prefix = "consultants.survey.";
    private const string Suffix = ".claim";

    private readonly string _path;
    private readonly string _id;

    private ConsultantsSurveyClaim(bool taken, string path, string id, string why)
    {
        Taken = taken;
        _path = path;
        _id = id;
        Why = why;
    }

    /// <summary>Not decided yet — what a caller holds before <see cref="Take"/> has answered. Releasing it does nothing.</summary>
    public static ConsultantsSurveyClaim Undecided { get; } = new(false, string.Empty, string.Empty, "the claim was not taken");

    /// <summary>Whether this start surveys.</summary>
    public bool Taken { get; }

    /// <summary>Why it does not, in a sentence for the log; empty when it does.</summary>
    public string Why { get; }

    /// <summary>Takes the claim for <paramref name="identity"/> in the window slot of <paramref name="nowUtc"/>, or says who has it.</summary>
    /// <remarks>
    /// A directory that cannot be written is not a reason to skip the survey — it is answered as taken, with nothing
    /// to release, so the survey runs exactly as it did before claims existed.
    /// </remarks>
    public static ConsultantsSurveyClaim Take(string healthDirectory, string identity, DateTime nowUtc, TimeSpan window)
    {
        var path = Path.Combine(healthDirectory, Prefix + Hash($"{identity}|{nowUtc.Ticks / window.Ticks}") + Suffix);
        var id = Guid.NewGuid().ToString("N");
        if (!TryCreate(healthDirectory, path, out var refused))
        {
            return refused;
        }

        // Ours from here on: what follows may fail, and none of it may turn this claim into somebody else's.
        Stamped(path, $"{id} pid {Environment.ProcessId} at {nowUtc:O}");
        Pruned(healthDirectory, path, nowUtc - (window * 2));

        return new(true, path, id, string.Empty);
    }

    /// <summary>Gives the claim back so the next start in this slot surveys — only while the file is still this claim's.</summary>
    public void Release()
    {
        if (IsOurs())
        {
            Quietly(() => File.Delete(_path));
        }
    }

    /// <summary>
    /// What makes two surveys the same survey: this build, this side, the settings file as written, and the client's
    /// <c>COAI_*</c> environment that layers over it — except what is secret (<c>*_KEY</c>, <c>*_TOKEN</c>,
    /// <c>*_SECRET</c>, <c>*_PASSWORD</c>: a survey uses none, and none belongs in a hash on disk) and what is per run
    /// (<c>COAI_CALLER_*</c>, which would make every start its own survey).
    /// </summary>
    public static string IdentityOf(string version, string side, string settingsFile, Func<IEnumerable<KeyValuePair<string, string>>> environment)
    {
        var settings = new FileInfo(settingsFile);
        var stamp = settings.Exists ? $"{settings.LastWriteTimeUtc.Ticks}:{settings.Length}" : "none";
        var variables = environment()
            .Where(pair => Counts(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}");

        return $"{version}|{side}|{stamp}|{string.Join('\n', variables)}";
    }

    /// <summary>The process's own environment, as pairs.</summary>
    public static IEnumerable<KeyValuePair<string, string>> ProcessEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Select(entry => new KeyValuePair<string, string>((string)entry.Key, entry.Value as string ?? string.Empty));

    private static readonly string[] SecretEndings = ["_KEY", "_TOKEN", "_SECRET", "_PASSWORD"];

    private static bool Counts(string name) =>
        name.StartsWith("COAI_", StringComparison.Ordinal)
        && !name.StartsWith("COAI_CALLER_", StringComparison.Ordinal)
        && !SecretEndings.Any(ending => name.EndsWith(ending, StringComparison.Ordinal));

    /// <summary>
    /// The create that decides: true when it made the file (ours); otherwise <paramref name="refused"/> is the answer —
    /// taken by another, or (the directory cannot be written) taken with nothing to release.
    /// </summary>
    private static bool TryCreate(string directory, string path, out ConsultantsSurveyClaim refused)
    {
        refused = Undecided;
        try
        {
            Directory.CreateDirectory(directory);
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read).Dispose();

            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            refused = new(false, path, string.Empty, "another server took the survey for this build and settings in this window");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            refused = new(true, string.Empty, string.Empty, string.Empty);
        }

        return false;
    }

    private bool IsOurs() =>
        Taken && _path.Length > 0 && Quietly(() => File.Exists(_path) && File.ReadAllText(_path).StartsWith(_id, StringComparison.Ordinal));

    private static void Stamped(string path, string text) => Quietly(() => File.WriteAllText(path, text));

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..20];

    private static void Pruned(string directory, string ours, DateTime olderThanUtc)
    {
        var stale = Quietly(() => Directory.EnumerateFiles(directory, Prefix + "*" + Suffix)
            .Where(claim => claim != ours && File.GetLastWriteTimeUtc(claim) < olderThanUtc).ToList(), []);
        foreach (var claim in stale)
        {
            // Another start pruned it first, or a reader holds it: the next take tries again.
            Quietly(() => File.Delete(claim));
        }
    }

    /// <summary>A file operation whose failure costs nothing but itself: an <see cref="IOException"/> or a refusal is false.</summary>
    private static bool Quietly(Action act) => Quietly(() => { act(); return true; }, false);

    private static bool Quietly(Func<bool> ask) => Quietly(ask, false);

    private static T Quietly<T>(Func<T> ask, T otherwise)
    {
        try
        {
            return ask();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return otherwise;
        }
    }
}
