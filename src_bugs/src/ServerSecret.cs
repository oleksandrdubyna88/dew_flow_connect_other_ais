namespace CoaiBugs;

/// <summary>The secret contributor keys are hashed with.</summary>
/// <remarks>
/// A type rather than a string so that it is never passed where a key or a hash belongs — the gate
/// takes a secret and a presented key in the same call, and a string for each is a transposition
/// the compiler cannot see. Read once from <c>COAI_BUGS_SECRET</c> at startup; never logged, never
/// an argument.
/// </remarks>
public sealed record ServerSecret(string Value);
