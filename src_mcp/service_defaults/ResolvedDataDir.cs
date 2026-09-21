namespace CoaiMcp.ServiceDefaults;

/// <summary>
/// A data directory that has been RESOLVED — the side applied, the trim applied, an unusable side
/// already refused — and never a file path.
/// </summary>
/// <remarks>
/// <para><b>Why a type.</b> A reviewer of story 1.3 asked for an opaque resolved-data-directory type
/// so that a ROOT path cannot satisfy the notices writer's seam, and story 1.4 answered yes once a
/// writer existed to pay for it. The mistake it exists to refuse is
/// <c>Append(DataRootFor(env), notice)</c> — the directory BEFORE the side is applied, which is the
/// one thing in this codebase that looks like a data directory and is not one. A structural test
/// that nobody composes the path by hand cannot see that call; a parameter type refuses it at
/// compile time.</para>
///
/// <para><b>The constructor is PRIVATE, and that is the code round's finding rather than a
/// preference.</b> With an internal constructor the type was minted by one method and mintable by
/// every method in the assembly — and <c>ServerNotices.Append(new(DataRootFor(env)), notice)</c>
/// compiles through a target-typed <c>new</c> that names the type nowhere, so no source scan can see
/// it and the census that asks which members RETURN the type does not either. A private constructor
/// makes that expression impossible to write instead of merely unlikely, and leaves exactly one door:
/// <see cref="For"/>, which is a named form a scan can count.</para>
///
/// <para><b>It throws, and <c>csharp/doctrine.md</c> §3 says a parse does not.</b> The rule's reason
/// is that "a malformed input is an expected answer" — and here it is not one. Nothing parses a
/// person's text into this: the only caller is <c>PanelSettings.DataDirectoryFor</c>, which has
/// already applied the trim, the side and the refusal, and cannot produce an empty or relative path.
/// A value that fails these checks is a defect at that one minting site, and the alternatives are
/// both worse than an exception: a null is what the no-null rule forbids, and an empty-valued
/// instance would send a person's notices to whatever directory launched the process — the exact
/// failure <c>ServerNotices.PathFor</c>'s own refusal exists to prevent, arriving silently.</para>
///
/// <para><b>Why it lives here.</b> <c>ServiceDefaults</c> is the library that already owns "a path
/// under the data directory" (<see cref="CoaiLogPath"/>) and the one every other project can
/// reference; in <c>Server</c> it would be unreachable from <c>Runners</c>, where the other ledger is.
/// <see cref="CoaiLogPath.RootFor"/> and <c>UsageLedger</c> keep taking a string all the same:
/// <c>coai-bugs</c> resolves its own directory by its own rule and calls <c>RootFor</c> with it, and
/// requiring the type there would make a second binary mint a value whose guarantee it does not
/// have — a worse lie than a string.</para>
///
/// <para><b>A record CLASS, not a positional record and not a struct.</b> A positional record
/// publishes a public constructor, which is the hole above; a <c>readonly record struct</c> can be
/// defeated by <c>default</c>, which carries a null path past every check. It validates the SHAPE —
/// non-empty, rooted — which is not the same as the directory being usable: <c>C:\data*</c> passes
/// here and fails at <c>CreateDirectory</c>, and the writer's exception list is what answers that.</para>
/// </remarks>
public sealed record ResolvedDataDir
{
    /// <summary>The directory, as the resolver decided it. A full path, never empty.</summary>
    public string Path { get; }

    private ResolvedDataDir(string path) => Path = path;

    /// <summary>
    /// The one door. Internal, so only the assembly holding the resolver can open it, and NAMED, so
    /// a census can count the places that do.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path is empty or relative — which the resolver cannot produce, so it is a defect at the
    /// minting site rather than an answer about the input. See the remarks on the type.
    /// </exception>
    internal static ResolvedDataDir For(string path) =>
        new(Checked(path));

    private static string Checked(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException(
                "a resolved data directory cannot be empty: combined with a file name it is the bare "
                + "name, which lands beside whatever launched this process instead of where the "
                + "extension reads it", nameof(path))
            : System.IO.Path.IsPathRooted(path)
                ? path
                : throw new ArgumentException(
                    $"'{path}' is not rooted, so it was composed by hand rather than resolved — the "
                    + "resolver always answers a full path, and a relative data directory moves with "
                    + "the working directory of whichever process was launched", nameof(path));
}
