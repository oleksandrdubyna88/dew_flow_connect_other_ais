namespace CoaiBugs;

/// <summary>A key's id: the one thing about a contributor the server ever names.</summary>
/// <remarks>
/// Sixteen hex digits when this server minted it; an id typed into <c>--revoke --id</c> may be
/// anything, and an id that names no key simply matches no row — so there is nothing to validate
/// here, and a positional record is enough. It is a type so that a hash, a note or a raw key cannot
/// be passed where an id belongs, which in an audit row about an administrator would put a secret
/// into an exact-time record.
/// </remarks>
public sealed record KeyId(string Value);
