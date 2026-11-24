using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server.Keys;

/// <summary>What a pick came to. A closed set: a key, no key at all, keys that cannot be used now, or a pool that could not be read.</summary>
internal abstract record PickResult
{
    private PickResult()
    {
    }

    /// <summary>The next usable key after the cursor. <see cref="Secret"/> prints as <c>claude key &lt;id&gt;</c>.</summary>
    public sealed record Picked(ClaudeKeySecret Secret, string Label) : PickResult;

    /// <summary>The pool holds no key at all (a disabled or dead key is still a key, so that case is <see cref="Unusable"/>).</summary>
    public sealed record Empty : PickResult;

    /// <summary>Keys exist and none can be used now. <see cref="EarliestFreeUtc"/> is when the first cooling one is free again, or null when none will be without a person.</summary>
    public sealed record Unusable(DateTime? EarliestFreeUtc, KeyCounts Counts) : PickResult;

    /// <summary>The pool could not be used (busy past two seconds, or a disk fault): the launch stops as <c>crash</c> with this sentence.</summary>
    public sealed record Failed(string Sentence) : PickResult;
}

/// <summary>How a launch that carried a key ended, as far as the key is concerned.</summary>
internal enum KeyLaunchEnd
{
    /// <summary>It ended well: the cooldown and its count are cleared.</summary>
    Succeeded,

    /// <summary>The vendor said the account is at its limit: cooldown until the stated reset, else a guess.</summary>
    Limit,

    /// <summary>A transient throttle: 60 seconds, no count.</summary>
    Throttled,

    /// <summary>The vendor refused the key itself (only a signal with a recorded fixture): the key is dead (auth).</summary>
    AuthRefused,

    /// <summary>The exact text of the key was found where it must never be: the key is dead (exposed).</summary>
    Exposed,

    /// <summary>A network error, a 5xx, a timeout or a crash: nothing happens to the key. It has a name so the caller says it, and a test can say it never kills a key.</summary>
    Transient,
}

/// <summary>What the caller records of one launch: how it ended, the vendor's own words for a limit, and the reset the vendor stated (<c>UsageLedger.StatedResetOf</c>), when it did.</summary>
internal sealed record KeyLaunchReport(KeyLaunchEnd End, string Reason = "", DateTimeOffset? StatedReset = null);

/// <summary>
/// The round robin over the keys of one device (KEY_POOL_SPEC.md section 5): the next usable key after the persisted cursor for a fresh session, the same key for a resumed one, a cooldown after a limit,
/// dead after a refusal, and no wait when nothing is usable.
/// </summary>
/// <remarks>
/// <para><b>One pick is a few milliseconds under <c>pool.lock</c></b> and holds nothing afterwards: no lease per key, so a crash after a pick strands nothing. A setup-token is not refreshed in place, so two sessions on one key
/// do not race for a refresh token; the round robin only spreads the launches.</para>
/// <para><b>The ring</b> is every key, ordered by added time and then id; a pick starts after the cursor and takes the first usable one. A cursor whose key is gone starts at the first.</para>
/// <para><b>The token leaves only as a <see cref="ClaudeKeySecret"/></b>, from <see cref="Pick"/> and <see cref="Resume"/>, to the launch road.</para>
/// </remarks>
internal sealed class KeyPool(KeyStoreAccess access)
{
    /// <summary>How long a throttle (a transient rate limit) keeps a key out.</summary>
    internal static readonly TimeSpan ThrottleCooldown = TimeSpan.FromSeconds(60);

    /// <summary>The next key for a FRESH session.</summary>
    public PickResult Pick()
    {
        if (!access.Folder.Exists)
        {
            return new PickResult.Empty();
        }

        var run = access.Run(create: false, session => KeyResult<PickResult>.Of(PickUnderLock(session)));

        return run.Value ?? new PickResult.Failed(run.Refusal);
    }

    private PickResult PickUnderLock(KeySession session)
    {
        var now = access.NowUtc;
        var ring = session.Entries();
        if (ring.Count == 0)
        {
            return new PickResult.Empty();
        }

        var pool = session.Pool();
        var start = IndexAfter(ring, pool.Cursor);
        for (var step = 0; step < ring.Count; step++)
        {
            var entry = ring[(start + step) % ring.Count];
            if (entry.Usable(now) && session.ReadSecret(entry.Record.Id) is { } secret)
            {
                Picked(session, entry.Record, pool, now);

                return new PickResult.Picked(secret, entry.Record.Label);
            }
        }

        return NoneUsable(session, ring, pool, now);
    }

    private static int IndexAfter(IReadOnlyList<KeyEntry> ring, string cursor)
    {
        for (var index = 0; index < ring.Count; index++)
        {
            if (ring[index].Record.Id == cursor)
            {
                return (index + 1) % ring.Count;
            }
        }

        return 0;
    }

    private static void Picked(KeySession session, KeyRecord key, PoolFile pool, DateTime now)
    {
        session.SaveRecord(key with { LastUsedUtc = now, Launches = key.Launches + 1 });
        session.SavePool(pool with { Cursor = key.Id });
    }

    private PickResult NoneUsable(KeySession session, IReadOnlyList<KeyEntry> ring, PoolFile pool, DateTime now)
    {
        var counts = KeyCounts.Of(ring, now);
        var earliest = ring
            .Where(entry => entry.Record.State == KeyStates.Enabled && entry.Problem.Length == 0 && entry.Record.InCooldownAt(now))
            .Select(entry => entry.Record.CooldownUntilUtc)
            .Min();
        if (pool.ExhaustedNoticeUtc is not { } last || now - last >= KeyNotices.ExhaustedEvery)
        {
            access.Notify(KeyNotices.Exhausted(counts, earliest, now));
            session.SavePool(pool with { ExhaustedNoticeUtc = now });
        }

        return new PickResult.Unusable(earliest, counts);
    }

    /// <summary>
    /// The key a session already has, for a RESUMED turn: the same key while it exists and is not dead — also when it is disabled or cooling. A removed, dead or missing key is refused ("gone"), and the caller's
    /// loop goes on as after a limit.
    /// </summary>
    public KeyResult<ClaudeKeySecret> Resume(string keyId)
    {
        if (!KeyIds.IsValid(keyId) || !access.Folder.Exists)
        {
            return KeyResult<ClaudeKeySecret>.Refused(Gone);
        }

        return access.Run(create: false, session =>
        {
            var entry = session.Entries().FirstOrDefault(candidate => candidate.Record.Id == keyId);

            return entry is { Record.State: not KeyStates.Dead } && session.ReadSecret(keyId) is { } secret
                ? KeyResult<ClaudeKeySecret>.Of(secret)
                : KeyResult<ClaudeKeySecret>.Refused(Gone);
        });
    }

    private const string Gone = "gone: the key of this session was removed, is dead, or its file is missing";

    /// <summary>Records how a launch that carried <paramref name="keyId"/> ended, and applies it: a cooldown, a count, or the dead state with its notice.</summary>
    public KeyResult<KeyRow> Record(string keyId, KeyLaunchReport report)
    {
        if (!KeyIds.IsValid(keyId) || !access.Folder.Exists)
        {
            return KeyResult<KeyRow>.Refused("no such key");
        }

        return access.Run(create: false, session =>
        {
            var entries = session.Entries();
            if (entries.FirstOrDefault(entry => entry.Record.Id == keyId) is not { } current)
            {
                return KeyResult<KeyRow>.Refused("no such key");
            }

            var now = access.NowUtc;
            var updated = Apply(current.Record, report, session.ReadSecret(keyId), now);
            session.SaveRecord(updated);
            AfterRecording(session, entries, current.Record, updated, report, now);

            return KeyResult<KeyRow>.Of(KeyStoreAccess.RowOf(current with { Record = updated }, now));
        });
    }

    private static KeyRecord Apply(KeyRecord key, KeyLaunchReport report, ClaudeKeySecret? secret, DateTime now) => report.End switch
    {
        KeyLaunchEnd.Succeeded => key with { CooldownUntilUtc = null, ConsecutiveCooldowns = 0 },
        KeyLaunchEnd.Limit => Limited(key, report, secret, now),
        KeyLaunchEnd.Throttled => key with { CooldownUntilUtc = Later(key.CooldownUntilUtc, now + ThrottleCooldown) },
        KeyLaunchEnd.AuthRefused => Dead(key, KeyDeadReasons.Auth, key.AuthStops + 1),
        KeyLaunchEnd.Exposed => Dead(key, KeyDeadReasons.Exposed, key.AuthStops),
        _ => key,
    };

    private static KeyRecord Limited(KeyRecord key, KeyLaunchReport report, ClaudeKeySecret? secret, DateTime now)
    {
        var until = report.StatedReset is { } stated && stated.UtcDateTime > now
            ? stated.UtcDateTime
            : CooldownParser.Until(report.Reason, new DateTimeOffset(now, TimeSpan.Zero), key.ConsecutiveCooldowns).UtcDateTime;
        var said = secret is null ? report.Reason : secret.Scrub(report.Reason);

        return key with
        {
            CooldownUntilUtc = Later(key.CooldownUntilUtc, until),
            ConsecutiveCooldowns = key.ConsecutiveCooldowns + 1,
            LimitStops = key.LimitStops + 1,
            Note = Redaction.SafeText(said, 200),
        };
    }

    private static KeyRecord Dead(KeyRecord key, string reason, int authStops) => key with
    {
        State = KeyStates.Dead,
        DeadReason = key.DeadReason == KeyDeadReasons.Exposed ? KeyDeadReasons.Exposed : reason,
        AuthStops = authStops,
    };

    private static DateTime Later(DateTime? current, DateTime wanted) => current is { } existing && existing > wanted ? existing : wanted;

    private void AfterRecording(KeySession session, IReadOnlyList<KeyEntry> entries, KeyRecord before, KeyRecord after, KeyLaunchReport report, DateTime now)
    {
        if (report.End == KeyLaunchEnd.Limit)
        {
            ShareTheLimit(session, entries, after);
        }

        if (before.State != KeyStates.Dead && after.State == KeyStates.Dead)
        {
            access.Notify(report.End == KeyLaunchEnd.Exposed ? KeyNotices.Exposed(after, now) : KeyNotices.Dead(after, now));
        }
        else if (report.End == KeyLaunchEnd.Exposed && before.DeadReason != KeyDeadReasons.Exposed)
        {
            // A key already dead by a refusal that is then found exposed: the exposure is the news.
            access.Notify(KeyNotices.Exposed(after, now));
        }
    }

    /// <summary>A limit is the ACCOUNT's: every other key of the same account cools down to the same time (never shorter than it already had).</summary>
    private static void ShareTheLimit(KeySession session, IReadOnlyList<KeyEntry> entries, KeyRecord limited)
    {
        if (limited.Account.Length == 0 || limited.CooldownUntilUtc is not { } until)
        {
            return;
        }

        foreach (var sibling in entries.Select(entry => entry.Record).Where(other => other.Id != limited.Id && other.State == KeyStates.Enabled && other.Account.Equals(limited.Account, StringComparison.OrdinalIgnoreCase)))
        {
            session.SaveRecord(sibling with { CooldownUntilUtc = Later(sibling.CooldownUntilUtc, until) });
        }
    }
}
