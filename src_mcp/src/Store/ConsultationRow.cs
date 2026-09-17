namespace CoaiMcp.Store;

/// <summary>
/// One consultation as the database holds it — the record file's shape, flattened and totalled.
/// </summary>
/// <remarks>
/// <para>A type of its own rather than the server's <c>ConsultationRecord</c>, so this project keeps
/// owning its own tables: the record is a conversation with a list of turns in it, and the row is
/// what the log page asks a question of. The mapping lives with the record, where the turns are.</para>
/// <para>Every field is a plain value and nothing is nullable except the money, which is nullable for
/// the reason a round's is: a vendor that does not report what it charged leaves a BLANK, and a zero
/// there would read as "this was free".</para>
/// </remarks>
public sealed record ConsultationRow(
    string Id,
    string Caller,
    string CallerKind,
    string RepoPath,
    string Branch,
    string HeadSha,
    string Vendor,
    string Model,
    int Turns,
    string Status,
    string Reason,
    /// <summary>solved / not_solved / abandoned / lapsed, or empty for one nobody spoke about.</summary>
    string Outcome,
    /// <summary>caller / person / server, or empty wherever <see cref="Outcome"/> is.</summary>
    string OutcomeBy,
    string StartedUtc,
    string EndedUtc,
    double Seconds,
    long TokensIn,
    long TokensOut,
    double? CostUsd,
    string Problem,
    string Advice,
    string Alert);
