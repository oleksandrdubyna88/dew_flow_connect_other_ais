using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>One vendor the operator has paid for and signed in on this machine.</summary>
/// <param name="Id">The name a client asks for, and the folder its accounts live under.</param>
/// <param name="Runtime">Which CLI adapter drives it — one of <see cref="VendorConfig.KnownRuntimes"/>.</param>
/// <param name="Models">
/// The allowlist. This is the only thing between a caller and an arbitrary <c>--model</c> string on
/// somebody else's subscription, so it is checked on the way in rather than trusted.
/// </param>
/// <param name="Slots">
/// The account directories, in the order the operator wrote them. Several accounts of one vendor are
/// how a company gets past one subscription's rate limit; one is the normal case.
/// </param>
/// <remarks>
/// There is deliberately NO <c>slotConcurrency</c>. The draft had one, defaulting to 1, and the plan
/// round was right that it is a trap: the entire story exists because two processes on one account
/// race to rotate the same OAuth refresh token and the loser gets <c>invalid_grant</c>. An option
/// whose only safe value is the default is not an option, it is a loaded gun with a note on it.
/// Exclusion is an invariant here, enforced by <see cref="SlotRegistry"/>'s lock, not a setting.
/// </remarks>
public sealed record VendorConfig(
    string Id,
    string Runtime,
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Slots)
{
    /// <summary>
    /// The runtimes a vendor may name, taken from the adapters that exist rather than typed again.
    /// </summary>
    /// <remarks>
    /// <c>local</c> is in <see cref="ReviewerRuntimeSelector.RuntimeNames"/> and is refused here: it
    /// points at a local model endpoint, which is not a subscription and has no account to sign in.
    /// A Team server exists to share paid CLIs; naming <c>local</c> would configure something this
    /// server cannot run.
    /// </remarks>
    public static IReadOnlySet<string> KnownRuntimes { get; } =
        new HashSet<string>(
            ReviewerRuntimeSelector.RuntimeNames.Where(r => !r.Equals("local", StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Why this entry cannot be used, or empty when it can.</summary>
    /// <remarks>
    /// Validation happens at LOAD, never at use. A model list that turns out to be empty at the
    /// moment somebody asks for a review is an outage; the same fact at load is a log line and a
    /// sentence in the catalog, while the previous good configuration keeps serving.
    /// </remarks>
    public string Refusal()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return "a vendor has no 'id'";
        }

        if (!KnownRuntimes.Contains(Runtime ?? string.Empty))
        {
            return $"vendor '{Id}' names runtime '{Runtime}', which is not one of "
                + string.Join(", ", KnownRuntimes.OrderBy(r => r, StringComparer.Ordinal));
        }

        return FirstListRefusal();
    }

    private string FirstListRefusal()
    {
        if (Models is not { Count: > 0 } || Models.Any(string.IsNullOrWhiteSpace))
        {
            return $"vendor '{Id}' has no 'models' — an empty allowlist refuses every review, "
                + "which looks like an outage with no error";
        }

        if (Slots is not { Count: > 0 })
        {
            return $"vendor '{Id}' has no 'slots' — name at least one account directory, e.g. [\"a\"]";
        }

        return Slots.FirstOrDefault(s => !IsSafeSegment(s)) is { } bad
            ? $"vendor '{Id}' has slot '{bad}': a slot is one path segment of letters, digits, "
                + "'-' or '_', because it names a directory under the data folder"
            : string.Empty;
    }

    /// <summary>
    /// A slot name becomes a directory, so it may not be able to leave the data folder.
    /// </summary>
    /// <remarks>
    /// An allowlist of characters rather than a search for <c>..</c>: the second is a blocklist, and
    /// a blocklist on a path is a bet that you thought of every separator, encoding and alternate
    /// stream the platform has. This is the operator's own file, so the check is not a security
    /// boundary against an attacker — it is the difference between a typo caught at load and a
    /// directory created somewhere nobody will look for it.
    /// </remarks>
    private static bool IsSafeSegment(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
