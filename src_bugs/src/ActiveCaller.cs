namespace CoaiBugs;

/// <summary>One caller sending inside the current window, as the limiter sees it.</summary>
/// <remarks>
/// <para>A record and not a tuple. It was
/// <c>IReadOnlyList&lt;(string Subject, int InWindow, bool Limited)&gt;</c>, which a code round
/// called out against the doctrine: a data container is a <c>record</c>, and a three-field tuple
/// crossing a public boundary is a shape nothing can document, no caller can name, and every reader
/// has to count positions in.</para>
/// <para><b>It carries no order.</b> The limiter answers who is sending; <c>/admin/active</c>
/// decides that the busiest go first, because that ordering is an answer to "who is flooding us"
/// and not a fact about a window. The limiter sorted its own rows and the route then re-sorted the
/// merged list with the same comparator — the same duplication, one layer apart.</para>
/// </remarks>
/// <param name="Subject">
/// The limiter subject with its prefix intact: <c>key:…</c> for a contributor, <c>admin-…</c> for
/// an administrator. The prefix is what tells a reader which kind of caller a row is about.
/// </param>
/// <param name="InWindow">How many requests it has been admitted for inside the window.</param>
/// <param name="Limited">Whether it is at or over its own limit right now.</param>
public sealed record ActiveCaller(string Subject, int InWindow, bool Limited);
