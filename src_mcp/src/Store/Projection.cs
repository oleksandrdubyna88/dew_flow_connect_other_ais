namespace CoaiMcp.Store;

/// <summary>
/// A write to the rounds database, which is never allowed to matter.
/// </summary>
/// <remarks>
/// <para>The session and consultation FILES are the source of truth; this database is a projection
/// of them, for the log page and the two questions it answers. So a database that is locked, full or
/// corrupt is a line in the log — never a failed review and never a consultation that refuses to
/// answer somebody who is stuck.</para>
/// <para>Its own type since 2026-09-13, because a second writer arrived: the consultation service
/// projects a record on every state change, and it is not a collaborator of the panel service that
/// owned this `try`/`catch`. Copying eight lines would have been copying the DECISION with them, and
/// the decision is the part that matters — the copy is where somebody one day lets a projection
/// failure escape.</para>
/// </remarks>
public sealed class Projection(string dataDir, Serilog.ILogger log)
{
    /// <summary>Opens the database, hands it to the writer, and swallows every way that can fail.</summary>
    /// <param name="what">
    /// What was being projected, for the log line. A sentence a person can act on — "the round", "the
    /// consultation" — rather than a stack trace with no subject.
    /// </param>
    public void Write(Action<RoundsDb> write, string what = "the round")
    {
        try
        {
            using var db = RoundsDb.Open(dataDir, log);
            if (db is not null)
            {
                write(db);
            }
        }
        catch (Exception e)
        {
            log.Warning(e, "{What} could not be projected into the rounds database", what);
        }
    }
}
