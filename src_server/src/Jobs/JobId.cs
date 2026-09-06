using System.Globalization;

namespace CoaiServer;

/// <summary>What a poll for an id this server does not hold actually means.</summary>
public enum MissingJob
{
    /// <summary>No such job, and no reason to think there ever was one.</summary>
    Unknown,

    /// <summary>It belonged to an earlier run of this server, which means it died with it.</summary>
    Lost,
}

/// <summary>
/// A job id that carries the server run it belongs to.
/// </summary>
/// <remarks>
/// <para><b>Why the epoch is in the id.</b> Jobs live in memory, so a restart forgets every one of
/// them. Without the epoch every poll afterwards answers "unknown", and a client cannot tell "you
/// made that id up" from "your review was running and the server died underneath it" — which are
/// opposite instructions: resubmit, or report a real failure to a person. With the epoch it costs
/// nothing to store and the answer is exact.</para>
/// <para><b>An epoch that is not plausibly a past run reads as UNKNOWN.</b> The first draft compared
/// only for inequality, so a typo, a truncated id or a client with a skewed clock produced
/// <c>lost</c> — and <c>lost</c> is the answer that makes automated clients start recovery. An id is
/// only <c>lost</c> when its epoch parses AND is strictly earlier than this run's. Raised on the plan
/// round; the id is caller-supplied text and was being trusted to mean something.</para>
/// </remarks>
public static class JobId
{
    /// <summary>This process's run, as the ticks it started at.</summary>
    public static readonly long Epoch = DateTimeOffset.UtcNow.UtcTicks;

    public static string New() => New(Epoch);

    public static string New(long epoch) =>
        epoch.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");

    /// <summary>What to say about an id this server is not holding.</summary>
    public static MissingJob Classify(string id, long thisEpoch)
    {
        var dash = (id ?? string.Empty).IndexOf('-');
        if (dash <= 0)
        {
            return MissingJob.Unknown;
        }

        return long.TryParse(id![..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var epoch)
            && epoch > 0
            && epoch < thisEpoch
                ? MissingJob.Lost
                : MissingJob.Unknown;
    }

    /// <summary>The sentence a client shows a person, verbatim.</summary>
    public static string Explain(MissingJob missing) => missing == MissingJob.Lost
        ? "lost — the server restarted while this review ran. Nothing was collected; submit it again."
        : "unknown — no review with that id is on this server.";
}
