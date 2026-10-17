namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// THE A2-A3 SEAM (§0 of both cornerstone specs of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): what a survivor marker does to a free lock. A Kimi launch's locks cannot see a descendant that
/// inherited nothing (L-K5) or sits in its own session (L-K4); only A3's survivor marker can. A2 asks THIS interface in three places before it treats a free lock as a free sign-in —
/// <c>KimiLaunch.TakeAsync</c> (after the gate), <c>KimiShimHold.Taken</c> (after both tree files) and <c>KimiHome.Alive</c> (a marker keeps the home) — and in the retirement sweep; A3 implements it.
/// </summary>
/// <remarks>
/// Both answers are SENTENCES, so no caller depends on A3's marker types: empty means nothing stands, a sentence means the turn is given up (or the home / lock files are kept) with those words.
/// </remarks>
public interface IKimiSurvivors
{
    /// <summary>
    /// Empty when no survivor marker stands for the sign-in whose marker file is <paramref name="markerPath"/> (none exists, or it was reconciled and cleared); else the Busy sentence. A3's
    /// <c>KimiSurvivorGate.ReconcileAsync</c> is the real one: it ends what it can end by identity, confirms, and clears only on a confirmed-empty tree.
    /// </summary>
    Task<string> BlockedAsync(string markerPath, CancellationToken ct);

    /// <summary>Empty when no marker names the owned home <paramref name="ownedRoot"/>; else why the home is kept (<c>KimiHome.Alive</c>, beside the home's tree hold). Never opens the sign-in folder.</summary>
    string KeepsHome(string ownedRoot);
}

/// <summary>Where the seam is read from: the product's gate, unless a test says otherwise on this async flow.</summary>
public static class KimiSurvivors
{
    private static readonly AsyncLocal<IKimiSurvivors?> _gate = new();

    /// <summary>The gate: the product’s <see cref="ReconcilingSurvivors"/> over this host’s listing and lock root, unless a test says otherwise on this async flow.</summary>
    public static IKimiSurvivors Gate => _gate.Value ?? ReconcilingSurvivors.Real;

    /// <summary>TEST ONLY: <paramref name="gate"/> as the seam for this async flow until the result is disposed.</summary>
    internal static IDisposable Using(IKimiSurvivors gate)
    {
        var before = _gate.Value;
        _gate.Value = gate;

        return new Restore(before);
    }

    private sealed class Restore(IKimiSurvivors? before) : IDisposable
    {
        public void Dispose() => _gate.Value = before;
    }
}
