namespace CoaiMcp.Server;

/// <summary>
/// What one read of the settings file found — a closed union, so a caller cannot forget the busy case.
/// </summary>
/// <remarks>
/// <para><b>Why a union and not an empty dictionary.</b> <see cref="SettingsFile.Read(string)"/> answers EMPTY for every
/// failure, so a file its writer still held open read as "no settings" and the host published the defaults under
/// the new stamp (C14 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>). A reload has to tell
/// "nothing is written there" from "somebody is writing it right now".</para>
/// <para><see cref="Busy"/> is ONLY a sharing or lock violation. A permission denial, a malformed file and an empty file
/// are <see cref="Values"/> with whatever could be read (nothing): the person who locked a file down by mistake, or
/// wrote a broken one, must see the defaults and fix it — never have the last good settings pinned for ever.</para>
/// </remarks>
internal abstract record SettingsRead
{
    private SettingsRead()
    {
    }

    /// <summary>The file was read; <paramref name="Settings"/> is empty for a malformed, empty or denied file.</summary>
    public sealed record Values(Dictionary<string, string> Settings) : SettingsRead;

    /// <summary>There is no file (or no folder), the normal case before the panel has ever been opened.</summary>
    public sealed record Missing : SettingsRead;

    /// <summary>Another handle holds the file: the open was refused by a sharing or lock violation. Try again later.</summary>
    public sealed record Busy : SettingsRead;
}

/// <summary>The settings layer of one reload — built from a file that could be read, or not built because it was busy.</summary>
internal abstract record SettingsLayer
{
    private SettingsLayer()
    {
    }

    /// <summary>The environment-over-file lookup, exactly what <see cref="SettingsFile.Layer(string, Func{string, string?}, Action{string})"/> returns.</summary>
    public sealed record Layered(Func<string, string?> Lookup) : SettingsLayer;

    /// <summary>The file is held by its writer; the caller keeps what it has.</summary>
    public sealed record Busy : SettingsLayer;
}
