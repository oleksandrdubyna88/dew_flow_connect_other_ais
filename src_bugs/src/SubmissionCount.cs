namespace CoaiBugs;

/// <summary>How many accepted ingests a key has made, over its lifetime.</summary>
/// <remarks>
/// A count without a clock, and not a rate limit — the limit is the limiter's minute. A struct
/// because it travels beside a month on every read of a key, and a type so that it cannot be
/// confused with a limit, a page size or a row id in a signature that takes an <c>int</c>.
/// </remarks>
public readonly record struct SubmissionCount(int Value);
