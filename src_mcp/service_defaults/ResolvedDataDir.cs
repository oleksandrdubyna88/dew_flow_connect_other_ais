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
/// <para><b>Minted by <c>PanelSettings.DataDirectoryFor</c> and by nothing else.</b> The constructor
/// is internal and this library grants <c>coai-mcp</c> alone (and its tests), so no other project can
/// mint a value whose guarantee comes from a resolver it does not share; inside <c>coai-mcp</c>, a
/// test counts the minting sites and refuses a second. <c>DataRootFor</c> keeps returning a string,
/// deliberately — giving it the type would make the type describe the confusion instead of
/// preventing it.</para>
/// <para><b>Why it lives here.</b> <c>ServiceDefaults</c> is the library that already owns "a path
/// under the data directory" (<see cref="CoaiLogPath"/>) and the one every other project can
/// reference; in <c>Server</c> it would be unreachable from <c>Runners</c>, where the other ledger is.
/// <see cref="CoaiLogPath.RootFor"/> and <c>UsageLedger</c> keep taking a string all the same:
/// <c>coai-bugs</c> resolves its own directory by its own rule and calls <c>RootFor</c> with it, and
/// requiring the type there would make a second binary mint a value whose guarantee it does not
/// have — a worse lie than a string. The type is required by exactly one seam, the notices writer,
/// and placed where the others can adopt it when their resolvers converge.</para>
/// <para><b>A record CLASS, not a struct.</b> A <c>readonly record struct</c> can be defeated by
/// <c>default</c>, which carries a null path past every check; a class with a validating constructor
/// cannot. It validates the SHAPE — non-empty, rooted — which is not the same as the directory being
/// usable: <c>C:\data*</c> passes here and fails at <c>CreateDirectory</c>, and the writer's
/// exception list is what answers that.</para>
/// </remarks>
public sealed record ResolvedDataDir
{
    /// <summary>The directory, as the resolver decided it. A full path, never empty.</summary>
    public string Path { get; }

    internal ResolvedDataDir(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "a resolved data directory cannot be empty: combined with a file name it is the bare "
                + "name, which lands beside whatever launched this process instead of where the "
                + "extension reads it", nameof(path));
        }

        if (!System.IO.Path.IsPathRooted(path))
        {
            throw new ArgumentException(
                $"'{path}' is not rooted, so it was composed by hand rather than resolved — the "
                + "resolver always answers a full path, and a relative data directory moves with the "
                + "working directory of whichever process was launched", nameof(path));
        }

        Path = path;
    }
}
