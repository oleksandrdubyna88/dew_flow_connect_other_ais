namespace CoaiBugs;

/// <summary>What revoking a key came to — three outcomes, because two of them are not failures.</summary>
/// <remarks>
/// The admin route maps these straight through: <see cref="NoSuchKey"/> is a 404,
/// <see cref="Now"/> is a 200 with <c>changed: true</c>, and <see cref="Already"/> is a 200 with
/// <c>changed: false</c> — idempotent, because an administrator pressing revoke twice has not made a
/// mistake and should not be told they have.
/// </remarks>
public abstract record Revoked
{
    private Revoked()
    {
    }

    /// <summary>No key has this id. The only one of the three that is an error.</summary>
    public sealed record NoSuchKey : Revoked;

    /// <summary>It was in force and now is not. This is the one that writes an audit row.</summary>
    public sealed record Now(UtcInstant At) : Revoked;

    /// <summary>It was already revoked, at the time carried here — not the time of this attempt.</summary>
    /// <remarks>Nothing is written: there was no mutation to audit, so the trail gains no row.</remarks>
    public sealed record Already(UtcInstant At) : Revoked;
}
