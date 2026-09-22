namespace CoaiMcp.Server;

/// <summary>
/// A setting whose value this build could not use, and the key it is about.
/// </summary>
/// <remarks>
/// The key exists so the notice written for this can be GROUPED. The extension keys repeats on
/// <c>(code, subject)</c>: with the key as the subject, two malformed settings are two rows and one
/// misconfiguration across ten restarts is one row with a count; without it, every unrecognised
/// setting there has ever been is a single row nobody can read. (Story 2.3.3's plan round.)
/// </remarks>
/// <param name="Key">The environment variable, exactly as it is spelled — never parsed from prose.</param>
/// <param name="Sentence">What to do about it, in the words a person reads.</param>
public sealed record UnrecognisedSetting(string Key, string Sentence);

/// <summary>
/// Something about the DISK that a person would want told: a database left behind, a new directory.
/// </summary>
/// <remarks>
/// <para><b>It carries a place as well as a kind.</b> gemini, on the plan round: with the kind alone
/// as the subject, two loose databases under different roots — which a machine running two sides
/// has — collapse into one row with a count of two, and the person cannot see WHICH. The subject is
/// <c>kind:place</c>, so each place is its own row and the same place across restarts is one.</para>
/// <para>The place is canonicalised by the caller (<c>Path.GetFullPath</c>), so that the same
/// directory reached as <c>C:\data</c> and <c>C:/data</c> is one subject rather than two.</para>
/// </remarks>
/// <param name="Kind">One of <see cref="LooseDatabase"/> or <see cref="NewDirectory"/>.</param>
/// <param name="Place">The directory it is about — canonical, and part of the grouping key.</param>
/// <param name="Sentence">What to do about it, in the words a person reads.</param>
public sealed record StorageNote(string Kind, string Place, string Sentence)
{
    /// <summary>A database in the shared root, from the layout before sides existed.</summary>
    /// <remarks>
    /// The two kind names are the ones <c>shared/data-side-vectors.json</c> already uses, and
    /// <c>DataSideVectorTests</c> classified notes by SUBSTRING to get them — <c>Contains("coai.db")</c>.
    /// Naming them in the type makes that fixture's expectation a field comparison instead.
    /// </remarks>
    public const string LooseDatabase = "loose-database";

    /// <summary>This side's directory did not exist and is being created.</summary>
    public const string NewDirectory = "new-directory";

    /// <summary>The grouping key: which kind, about which place.</summary>
    public string Subject => $"{Kind}:{Place}";
}
