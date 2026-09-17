namespace CoaiBugs;

/// <summary>Who did an administrative thing, and when — the two facts an audit row adds to a mutation.</summary>
/// <remarks>
/// <para><b>Exact times, about administrators.</b> <see cref="At"/> is an exact instant, and that is
/// deliberate: this is a log about the people holding administrative power, not about the people
/// contributing. A contributor is never named here, because the server never holds a contributor's
/// name — only key ids — and the row's target is a <see cref="KeyId"/>.</para>
/// <para><b>Constructed only through <see cref="By"/>.</b> The instant comes from the host's injected
/// clock and the identity from <see cref="AdminId"/>'s two factories, so no caller — story 2's admin
/// API included — can put an arbitrary string, or an arbitrary time, into an exact-time record about
/// an administrator.</para>
/// </remarks>
public sealed record Audit
{
    /// <summary>Who: <see cref="AdminId.Cli"/> for a one-shot, a derived id for the admin API.</summary>
    public AdminId Who { get; }

    /// <summary>When, exactly.</summary>
    public UtcInstant At { get; }

    private Audit(AdminId who, UtcInstant at)
    {
        Who = who;
        At = at;
    }

    /// <summary>An audit stamped now, from the injected clock.</summary>
    public static Audit By(AdminId who, TimeProvider clock) => new(who, UtcInstant.Now(clock));
}

/// <summary>One administrative action, as the audit holds it — typed on the way out as on the way in.</summary>
/// <remarks>
/// <paramref name="Id"/> is also the cursor: the page after the one holding this row is read with
/// <c>before: Id</c>.
/// </remarks>
public sealed record AuditRow(long Id, AdminId Who, AuditAction Action, KeyId Target, UtcInstant At);
