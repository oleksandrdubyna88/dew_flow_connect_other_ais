using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server.Keys;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What one claude launch of a role job runs on (epoch 3 E2 s7 t5, KEY_POOL_SPEC.md sections 5 and 6): a key of the pool, the person's own sign-in by the device switch, nothing (another vendor), or a reason there is none. A closed set,
/// so a caller cannot forget a case. Every member prints without the text of a key: <see cref="Pool"/> holds a <see cref="ClaudeKeySecret"/>, which prints as <c>claude key &lt;id&gt;</c>.
/// </summary>
internal abstract record RoleKey
{
    private RoleKey()
    {
    }

    /// <summary>The id the attempt, the ledger line and the resume handle carry: the key's id, <see cref="RoleKeyRoad.PersonSignIn"/>, or empty when the launch carries no key.</summary>
    public abstract string KeyId { get; }

    /// <summary>The launch carries no key: another vendor's CLI.</summary>
    public sealed record None : RoleKey
    {
        public override string KeyId => string.Empty;
    }

    /// <summary>A key of the pool. The launch road (<see cref="RoleModelLauncher"/>) is the one place that takes its text.</summary>
    public sealed record Pool(ClaudeKeySecret Secret) : RoleKey
    {
        public override string KeyId => Secret.Id;
    }

    /// <summary>The person's own sign-in, by the device switch, with the pool empty: today's launch (the read-only bind, the person's home).</summary>
    public sealed record PersonSignIn : RoleKey
    {
        public override string KeyId => RoleKeyRoad.PersonSignInId;
    }

    /// <summary>
    /// The claude models of the route cannot run now and the loop must not wait: <see cref="Sentence"/> says why. <see cref="Exhausted"/> is true for keys that exist and all cool down or are dead — the case where a job whose route is claude only fails with
    /// the stop <c>limit</c> and the earliest free time; false for an empty pool or a pool that could not be read.
    /// </summary>
    public sealed record Skip(string Sentence, bool Exhausted) : RoleKey
    {
        public override string KeyId => string.Empty;
    }

    /// <summary>The key a RESUMED session runs on is gone (removed, dead, or its file is missing): the turn stops as a limit does and the loop goes on with the next key.</summary>
    public sealed record Gone(string Sentence) : RoleKey
    {
        public override string KeyId => string.Empty;
    }
}

/// <summary>
/// The road between a role job and the claude key pool (epoch 3 E2 s7 t5): which key a launch runs on, how the launch ended as the key is concerned, and the device switch that lets a machine with no key yet run on the person's own sign-in. It holds NO text of a key
/// except as the <see cref="ClaudeKeySecret"/> the pool hands it, which only <see cref="RoleModelLauncher"/> takes the text of.
/// </summary>
/// <remarks>
/// <para><b>A fresh session picks, a resumed one keeps.</b> <see cref="Fresh"/> asks the pool for the next usable key; <see cref="Resume"/> asks for the key a session already has. Nothing waits: a pool with no usable key is a <see cref="RoleKey.Skip"/> at once.</para>
/// <para><b>The switch</b> (<see cref="SwitchSetting"/>) acts ONLY while the pool holds no key at all — a disabled or dead key counts as a key — so a device with keys that cool down never spends the person's own limit on role jobs.</para>
/// </remarks>
internal sealed class RoleKeyRoad(KeyPool pool, KeyStoreAccess access, Func<string, string?> settings)
{
    /// <summary>The device switch: <c>1</c> lets a claude role job run on the person's own sign-in while the pool holds no key. It carries the <c>COAI_ROLE_</c> prefix, so a start hands it to its runner through <c>env.json</c>.</summary>
    public const string SwitchSetting = "COAI_ROLE_CLAUDE_PERSON_SIGN_IN";

    /// <summary>What the ledger line, the attempt and the handle say for a launch on the person's own sign-in.</summary>
    public const string PersonSignInId = "person-sign-in";

    /// <summary>The road of one job: the pool of its data folder, its clock, its notices.</summary>
    public static RoleKeyRoad For(IRoleRun run)
    {
        var access = new KeyStoreAccess(run.DataDir, run.Clock, notice => run.Notice(notice), sharedSetting: run.Settings(KeyFolder.SharedSetting) == "1");

        return new RoleKeyRoad(new KeyPool(access), access, run.Settings);
    }

    /// <summary>The road of a start: the pool of the data folder, no job yet.</summary>
    public static RoleKeyRoad For(string dataDir, TimeProvider clock, Func<ServerNotice, bool> notice, Func<string, string?> settings)
    {
        var access = new KeyStoreAccess(dataDir, clock, offered => notice(offered), sharedSetting: settings(KeyFolder.SharedSetting) == "1");

        return new RoleKeyRoad(new KeyPool(access), access, settings);
    }

    /// <summary>Whether the device switch is set to <c>1</c>.</summary>
    public bool SwitchIsSet => settings(SwitchSetting) == "1";

    /// <summary>The key of a FRESH session of <paramref name="model"/>: a pick for claude, nothing for any other vendor.</summary>
    public RoleKey Fresh(ModelRef model)
    {
        if (model.Vendor != "claude")
        {
            return new RoleKey.None();
        }

        switch (pool.Pick())
        {
            case PickResult.Picked picked:
                return new RoleKey.Pool(picked.Secret);
            case PickResult.Empty:
                return WhenEmpty();
            case PickResult.Unusable unusable:
                return new RoleKey.Skip(Unusable(unusable.EarliestFreeUtc, unusable.Counts), Exhausted: true);
            case PickResult.Failed failed:
                return new RoleKey.Skip($"the claude key pool could not be used: {failed.Sentence}", Exhausted: false);
            default:
                return new RoleKey.Skip("the claude key pool gave no answer", Exhausted: false);
        }
    }

    /// <summary>The key a RESUMED session of <paramref name="model"/> runs on: the same key while it exists and is not dead. <paramref name="keyId"/> empty (a session that started with no key) is no key.</summary>
    public RoleKey Resume(ModelRef model, string keyId)
    {
        if (model.Vendor != "claude")
        {
            return new RoleKey.None();
        }

        // A claude session with no key id cannot be resumed on one: None would be the person's own sign-in.
        if (keyId.Length == 0)
        {
            return new RoleKey.Gone("the session has no claude key to resume with");
        }

        if (keyId == PersonSignInId)
        {
            return new RoleKey.PersonSignIn();
        }

        var kept = pool.Resume(keyId);

        return kept.Value is { } secret ? new RoleKey.Pool(secret) : new RoleKey.Gone(kept.Refusal);
    }

    /// <summary>What a pool with no key at all means: the switch, or a refusal with the cure — and one notice per day either way.</summary>
    private RoleKey WhenEmpty()
    {
        var now = access.NowUtc;
        if (SwitchIsSet)
        {
            access.Notify(KeyNotices.PersonSignInUsed(now));

            return new RoleKey.PersonSignIn();
        }

        access.Notify(KeyNotices.Empty(now, switchIsSet: false));

        return new RoleKey.Skip("the claude key pool is empty and role jobs do not use the person's own sign-in: add a key (coai-mcp --keys add --label <name>), or set COAI_ROLE_CLAUDE_PERSON_SIGN_IN=1 on a device that has none yet", Exhausted: false);
    }

    private static string Unusable(DateTime? earliestFreeUtc, KeyCounts counts) =>
        $"no usable claude key ({counts.Sentence})" + (earliestFreeUtc is { } free ? $"; the earliest is free at {ServerNotice.Iso(free)} UTC" : "; none is free without a person");

    /// <summary>
    /// What <c>start_role</c> says about a route before it makes any folder (the spec: no wait, 2 seconds at most): empty when the route can start, else the refusal sentence. A route with another vendor always starts — the loop skips the claude models and runs
    /// that vendor; a route with only claude models is refused when the pool holds no key (and the switch is not set) or no key can be used now. Never picks, never moves the cursor.
    /// </summary>
    public string StartRefusal(IReadOnlyList<ModelRef> route)
    {
        if (route.Count == 0 || route.Any(model => model.Vendor != "claude"))
        {
            return string.Empty;
        }

        switch (pool.Check())
        {
            case KeyAvailability.Usable:
                return string.Empty;
            case KeyAvailability.Empty:
                if (SwitchIsSet)
                {
                    return string.Empty;
                }

                access.Notify(KeyNotices.Empty(access.NowUtc, switchIsSet: false));

                return "the claude key pool of this device is empty and role jobs do not use the person's own sign-in: add a key (coai-mcp --keys add --label <name>, the token on stdin, or the Keys page); a device with no key yet can set COAI_ROLE_CLAUDE_PERSON_SIGN_IN=1";
            case KeyAvailability.Unusable unusable:
                return Unusable(unusable.EarliestFreeUtc, unusable.Counts) + ", and this role's route has no other vendor: nothing was started";
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// Records how a launch that carried a pool key ended, as far as the key is concerned (<see cref="KeyPool.Record"/>): a limit puts it in cooldown, a lost sign-in or a refused key makes it dead, a launch that ended well clears its cooldown, a
    /// text found in the output is an exposure. Anything else — a crash, a timeout, a refused preparation — leaves the key as it was. Never throws.
    /// </summary>
    public void Ended(RoleLaunched launched, RoleStop? stop)
    {
        try
        {
            // The text of ANOTHER pool key was in what the launch printed: that key is exposed too, whoever's launch it was.
            foreach (var other in launched.ExposedIds)
            {
                Exposed(other);
            }

            if (KeyIds.IsValid(launched.KeyId) && launched.Launch is not null)
            {
                var report = ReportOf(launched, stop, access.NowUtc);

                // The launch's OWN key found in its output: the one road for every exposure, so its mark waits for the lock and a mark that cannot be written is a notice.
                if (report.End == KeyLaunchEnd.Exposed)
                {
                    Exposed(launched.KeyId);
                }
                else
                {
                    pool.Record(launched.KeyId, report);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The pool could not be written: the launch's result stands, and the next pick reads the pool as it is.
        }
    }

    /// <summary>The report of one finished launch, from its stop and its own process result. Pure.</summary>
    internal static KeyLaunchReport ReportOf(RoleLaunched launched, RoleStop? stop, DateTime nowUtc)
    {
        if (launched.KeyExposed)
        {
            return new KeyLaunchReport(KeyLaunchEnd.Exposed);
        }

        // "Not logged in" is NOT read as a refused key (second judgement, deviation 2): on a keyed launch that text more probably means the CLI did not get the key, so one good key would die per job; a revoked key probably answers 401, which is
        // not read either. `KeyLaunchEnd.AuthRefused` waits for a recorded fixture of a real refused key (t3).
        if (stop is null)
        {
            return new KeyLaunchReport(KeyLaunchEnd.Succeeded);
        }

        if (stop.Word == RoleStopWords.Limit)
        {
            if (stop.Transient)
            {
                return new KeyLaunchReport(KeyLaunchEnd.Throttled, stop.Reason);
            }

            var stated = UsageLedger.StatedResetOf(launched.Launch?.Terminal, new DateTimeOffset(nowUtc, TimeSpan.Zero));

            return new KeyLaunchReport(KeyLaunchEnd.Limit, stop.Reason, DateTimeOffset.TryParse(stated, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var reset) ? reset : null);
        }

        // The model answered and the role did not like it (a refusal, an invalid answer): the vendor accepted the key, so the key is well. A crash, a timeout, a boundary stop: nothing is known of the key.
        return stop.Word is RoleStopWords.Refusal or RoleStopWords.OutputInvalid
            ? new KeyLaunchReport(KeyLaunchEnd.Succeeded)
            : new KeyLaunchReport(KeyLaunchEnd.Transient);
    }

    /// <summary>
    /// The exact text of the key of <paramref name="keyId"/> was found where it must never be (the coder's changed files, a launch's output): the key is dead (exposed), with ONE notice. The mark waits for the pool's lock as a writing verb does, and when it
    /// still cannot be written ONE notice of the same code says the key is STILL ENABLED and what to do by hand — an exposed key is never left enabled in silence. True when the mark is written (or the key is no longer in the pool, and its id is kept as exposed);
    /// false when it is not. Never throws.
    /// </summary>
    public bool Exposed(string keyId)
    {
        if (!KeyIds.IsValid(keyId))
        {
            return true;
        }

        string why;
        try
        {
            var marked = pool.Record(keyId, new KeyLaunchReport(KeyLaunchEnd.Exposed));

            // A refusal of the data kind is "no such key": the key left the pool while its session ran, and its id was kept as exposed before the pool looked for it. Every other refusal (busy, the folder refused) means nothing was written.
            if (marked.Ok || marked.Code == KeyExit.Data)
            {
                return true;
            }

            why = marked.Refusal;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            why = $"the key store could not be used: {e.Message}";
        }

        access.Notify(KeyNotices.ExposedNotMarked(keyId, why, access.NowUtc));

        return false;
    }

    /// <summary>The secret of a key the session still has, for the search of the coder's changed files; null when the key is gone.</summary>
    public ClaudeKeySecret? SecretOf(string keyId) =>
        KeyIds.IsValid(keyId) ? pool.Resume(keyId).Value : null;
}
