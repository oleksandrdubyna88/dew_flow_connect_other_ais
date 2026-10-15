using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// Everything a Kimi launch is checked against and built with before it starts — the ONE launch control every Kimi launch path goes
/// through (D8b, D10, D17, D18, D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; E4.2 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>).
/// STUB (red first): the launch itself is not built yet.
/// </summary>
public static class KimiLaunch
{
    public const string HomeVariable = "KIMI_CODE_HOME";

    public const string NoUpdateVariable = "KIMI_CODE_NO_AUTO_UPDATE";

    public const string NoUpdateOn = "1";

    public const string HomeFlag = "--home";

    public const string PersonsBinFlag = "--persons-bin";

    public const string ModelFlag = "--model";

    private const string ProfileVariable = "USERPROFILE";

    private const string HomeNameVariable = "HOME";

    private const string DriveVariable = "HOMEDRIVE";

    private const string PathVariable = "HOMEPATH";

    private const string SearchPath = "PATH";

    private const string D19 = "(D19)";

    /// <summary>How long the <c>--version</c> asked before every launch may take.</summary>
    public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(60);

    private static readonly string[] BuiltNames =
        [HomeVariable, NoUpdateVariable, ProfileVariable, HomeNameVariable, DriveVariable, PathVariable, SearchPath];

    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>D10, before the launch: why this model is not launched on, or empty when it is curated.</summary>
    public static string ModelRefusal(string model) => KimiModels.RefusalFor(model, KimiModels.Curated);

    /// <summary>
    /// D18: Kimi only where it was measured — a Windows host, starting a Windows <c>kimi</c>. Every number in the RESULTS is
    /// Windows; a WSL bridge named as the executable is the Linux side by another door. <c>VibeLaunch</c>'s two sentences, for Kimi.
    /// </summary>
    public static string HostRefusal(HostKind host, string executable) =>
        host != HostKind.Windows
            ? $"Kimi is refused on a {HostKinds.Word(host)} host until it is measured there (D18 of todo/PLAN_vibe_and_kimi_runtimes.md) — every Kimi measurement coai has is Windows"
            : HostKinds.RunsInWsl(executable)
                ? $"'{executable}' would start Kimi on the Linux side, which is refused until it is measured there (D18 of todo/PLAN_vibe_and_kimi_runtimes.md) — point this row at a Windows kimi"
                : string.Empty;

    /// <summary>
    /// <paramref name="request"/> in the BUILT environment (D19) — the one builder every Kimi child goes through, the version
    /// probe and the shim's own request alike, so the two cannot drift: nothing inherited but the minimal names, and exactly the
    /// seven this adds.
    /// </summary>
    /// <remarks>
    /// The parent's <c>PATH</c> is the request's own when it carries one, else this process's — the one environment value read
    /// here, and it is a <c>PATH</c> to be kept, never a location to be found (the person's paths are
    /// <see cref="KimiPlaces"/>'s, resolved by the runtime). In the shim that PATH is the one the runtime built, which
    /// <see cref="BuiltPath"/> leaves as it is.
    /// </remarks>
    public static ProcessRequest Built(ProcessRequest request, KimiPlaces places) => Built(request, places, ParentPath(request));

    /// <summary><see cref="Built(ProcessRequest, KimiPlaces)"/> over a parent <c>PATH</c> given — pure.</summary>
    public static ProcessRequest Built(ProcessRequest request, KimiPlaces places, string parentPath) => request with
    {
        InheritsEnvironment = false,
        Passthrough = ProcessEnvironment.Minimal,
        Environment = new Dictionary<string, string?>(ProcessEnvironment.NameComparer)
        {
            [HomeVariable] = places.Home,
            [NoUpdateVariable] = NoUpdateOn,
            [ProfileVariable] = places.Os,
            [HomeNameVariable] = places.Os,
            [DriveVariable] = Drive(places.Os),
            [PathVariable] = Rest(places.Os),
            [SearchPath] = BuiltPath([places.PersonsBin, .. places.ToolFolders], parentPath),
        },
    };

    /// <summary>
    /// The prefix, then each entry of <paramref name="parent"/> (split on the platform's separator, empties dropped) not already
    /// listed — compared as the platform compares paths, with trailing separators ignored. Idempotent: the built <c>PATH</c>
    /// built again is itself.
    /// </summary>
    public static string BuiltPath(IReadOnlyList<string> prefix, string? parent)
    {
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var entries = prefix.Concat((parent ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.Join(Path.PathSeparator, entries.Where(entry => entry.Length > 0 && seen.Add(entry.TrimEnd(Separators))));
    }

    /// <summary>
    /// D19: the launch is the built one. A path that built a Kimi launch any other way is refused here rather than started — the
    /// FIRST of: the structure (nothing inherited, the minimal names, exactly the seven), then each value, then — when the argv
    /// carries them — the two place flags. Each sentence names the value it found.
    /// </summary>
    public static string EnvironmentRefusal(ProcessRequest request, KimiPlaces places) =>
        new Func<string>[]
        {
            () => Structure(request),
            () => Same(request, HomeVariable, places.Home, "the owned home"),
            () => Same(request, ProfileVariable, places.Os, "coai's own os/ folder (the OS-home redirect)"),
            () => Same(request, HomeNameVariable, places.Os, "coai's own os/ folder (the OS-home redirect)"),
            () => DrivePath(request, places),
            () => Pinned(request),
            () => PathBegins(request, places),
            () => FlagIs(request, HomeFlag, places.OwnedRoot, "the owned root"),
            () => FlagIs(request, PersonsBinFlag, places.PersonsBin, "the person's bin"),
        }
        .Select(check => check())
        .FirstOrDefault(refusal => refusal.Length > 0) ?? string.Empty;

    /// <summary>The value after <paramref name="flag"/> in the arguments, or empty when the flag is absent or last.</summary>
    public static string FlagOf(ProcessRequest request, string flag)
    {
        var at = request.Arguments.ToList().IndexOf(flag);

        return at >= 0 && at + 1 < request.Arguments.Count ? request.Arguments[at + 1] : string.Empty;
    }

    /// <summary>The model the request names (<c>--model</c>).</summary>
    public static string ModelOf(ProcessRequest request) => FlagOf(request, ModelFlag);

    /// <summary>
    /// The warning for a Kimi version nobody measured — the same sentence on the launch (<see cref="LaunchTurn.Warning"/>) and on the
    /// row's card — or empty for a measured one (or when nothing printed a version).
    /// </summary>
    public static string VersionWarning(string printed) => WarningFor(VersionWarnings.VersionIn(printed));

    /// <summary>STUB (red first).</summary>
    public static Task<LaunchTurn> TakeAsync(HostKind host, ProcessRequest request, KimiPlaces places, IProcessLauncher launcher, CancellationToken ct) =>
        Task.FromResult(LaunchTurn.Free);

    private static string WarningFor(string version) =>
        version.Length == 0 || KimiAcpProfile.MeasuredVersions.Contains(version)
            ? string.Empty
            : VersionWarnings.Unmeasured(
                "Kimi", version, string.Join(", ", KimiAcpProfile.MeasuredVersions),
                $"every confinement number coai has — the write refusals, the owned home's inventory, the OS-home redirect (research/RESULTS_kimi_confinement.md) — is {string.Join(", ", KimiAcpProfile.MeasuredVersions)}'s",
                "re-run `node scripts/probe-kimi-confinement.mjs` with this Kimi to measure it");

    private static string ParentPath(ProcessRequest request) =>
        Value(request, SearchPath) is { Length: > 0 } own ? own : Environment.GetEnvironmentVariable(SearchPath) ?? string.Empty;

    private static string Drive(string os) => Root(os).TrimEnd(Separators);

    private static string Rest(string os) => os[Root(os).TrimEnd(Separators).Length..];

    private static string Root(string os) => Path.GetPathRoot(os) ?? string.Empty;

    /// <summary>The value a request carries for <paramref name="name"/> (names compared as the platform compares them), or empty.</summary>
    private static string Value(ProcessRequest request, string name) =>
        request.Environment.FirstOrDefault(pair => ProcessEnvironment.NameComparer.Equals(pair.Key, name)).Value ?? string.Empty;

    /// <summary>Nothing inherited but the platform's minimal names.</summary>
    private static bool Confined(ProcessRequest request) =>
        !request.InheritsEnvironment && request.Passthrough.SetEquals(ProcessEnvironment.Minimal);

    /// <summary>Exactly the seven built names — no more, none outside them.</summary>
    private static bool TheSeven(ProcessRequest request) =>
        request.Environment.Count == BuiltNames.Length && request.Environment.Keys.All(name => BuiltNames.Contains(name, ProcessEnvironment.NameComparer));

    private static string Structure(ProcessRequest request) =>
        Confined(request) && TheSeven(request)
            ? string.Empty
            : $"this Kimi launch was not built with coai's own environment (the minimal set plus {HomeVariable}, {NoUpdateVariable}=1, the four OS-home names pointing into coai's own os/ folder, and a PATH beginning with your ~/.kimi-code/bin, nothing inherited) and is refused {D19}";

    private static string Same(ProcessRequest request, string name, string expected, string what) =>
        Junctions.SameFolder(Value(request, name), expected)
            ? string.Empty
            : $"{name} is '{Value(request, name)}', not {what} '{expected}' — this Kimi launch is refused {D19}";

    private static string DrivePath(ProcessRequest request, KimiPlaces places) =>
        Value(request, DriveVariable) + Value(request, PathVariable) == places.Os
            ? string.Empty
            : $"{DriveVariable}+{PathVariable} is '{Value(request, DriveVariable)}{Value(request, PathVariable)}', not coai's own os/ folder '{places.Os}' — this Kimi launch is refused {D19}";

    private static string Pinned(ProcessRequest request) =>
        Value(request, NoUpdateVariable) == NoUpdateOn
            ? string.Empty
            : $"{NoUpdateVariable} is '{Value(request, NoUpdateVariable)}', not {NoUpdateOn} — Kimi would update itself under a launch coai measured on one version; this launch is refused {D19}";

    private static string PathBegins(ProcessRequest request, KimiPlaces places)
    {
        var first = Value(request, SearchPath).Split(Path.PathSeparator).FirstOrDefault() ?? string.Empty;

        return Junctions.SameFolder(first, places.PersonsBin)
            ? string.Empty
            : $"PATH begins with '{first}', not the person's bin '{places.PersonsBin}' — this Kimi launch is refused {D19}";
    }

    /// <summary>A place flag the argv carries must name the place the launch was resolved for; absent is admitted (<c>--version</c> carries neither).</summary>
    private static string FlagIs(ProcessRequest request, string flag, string expected, string what) =>
        FlagOf(request, flag) is { Length: > 0 } given && !Junctions.SameFolder(given, expected)
            ? $"{flag} is '{given}', not {what} '{expected}' — this Kimi launch is refused {D19}"
            : string.Empty;
}
