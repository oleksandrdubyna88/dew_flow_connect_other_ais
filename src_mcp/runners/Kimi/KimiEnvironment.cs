using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The BUILT environment of a Kimi launch (D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; E4.2 brief section 10): what
/// <see cref="Built(ProcessRequest, KimiPlaces)"/> makes for every Kimi child — the version probe and the shim's own request alike, so the
/// two cannot drift — and what <see cref="Refusal"/> admits, which is exactly that and nothing else. Its own unit, so the launch control
/// stays about the launch (deviation 24 of the E4 plan: the first draft split <c>KimiLaunch</c> in two partial files, which the coding
/// style forbids).
/// </summary>
public static class KimiEnvironment
{
    public const string HomeVariable = "KIMI_CODE_HOME";

    public const string NoUpdateVariable = "KIMI_CODE_NO_AUTO_UPDATE";

    public const string NoUpdateOn = "1";

    private const string ProfileVariable = "USERPROFILE";

    private const string HomeNameVariable = "HOME";

    private const string DriveVariable = "HOMEDRIVE";

    private const string PathVariable = "HOMEPATH";

    private const string SearchPath = "PATH";

    private const string D19 = "(D19)";

    private const string CacheVariable = "XDG_CACHE_HOME";

    /// <summary>The seven names a Windows launch is built from.</summary>
    private static readonly string[] WindowsNames =
        [HomeVariable, NoUpdateVariable, ProfileVariable, HomeNameVariable, DriveVariable, PathVariable, SearchPath];

    /// <summary>The five a Linux or WSL launch is built from (S-K8, DL11): <c>HOME</c> alone redirects the OS home, <c>XDG_CACHE_HOME</c> moves Kimi's native-file extraction out of it.</summary>
    private static readonly string[] UnixNames = [HomeVariable, NoUpdateVariable, HomeNameVariable, CacheVariable, SearchPath];

    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    /// <paramref name="request"/> in the BUILT environment (D19) — the one builder every Kimi child goes through, the version
    /// probe and the shim's own request alike, so the two cannot drift: nothing inherited but the minimal names, and exactly the
    /// names this adds — seven on Windows, five on Linux and WSL (<see cref="UnixNames"/>), by the machine's own operating system.
    /// </summary>
    /// <remarks>
    /// The parent's <c>PATH</c> is the request's own when it carries one, else this process's — the one environment value read
    /// here, and it is a <c>PATH</c> to be kept, never a location to be found (the person's paths are
    /// <see cref="KimiPlaces"/>'s, resolved by the runtime). In the shim that PATH is the one the runtime built, which
    /// <see cref="BuiltPath"/> leaves as it is. A launch that builds more than one request (the version probe beside the shim's) hands the first one's <see cref="PathOf"/> to the second, so one PATH source feeds both.
    /// </remarks>
    public static ProcessRequest Built(ProcessRequest request, KimiPlaces places) => Built(request, places, ParentPath(request));

    /// <summary><see cref="Built(ProcessRequest, KimiPlaces)"/> over a parent <c>PATH</c> given — pure.</summary>
    public static ProcessRequest Built(ProcessRequest request, KimiPlaces places, string parentPath) => Built(request, places, parentPath, OperatingSystem.IsWindows());

    /// <summary>
    /// The same for a stated platform — so the Windows shape and the Linux one are each a unit test on any machine. The launch is always built for the machine it runs on: a
    /// caller outside a test uses the overloads above.
    /// </summary>
    public static ProcessRequest Built(ProcessRequest request, KimiPlaces places, string parentPath, bool windows) => request with
    {
        InheritsEnvironment = false,
        Passthrough = ProcessEnvironment.Minimal,
        Environment = Variables(places, parentPath, windows),
    };

    private static Dictionary<string, string?> Variables(KimiPlaces places, string parentPath, bool windows)
    {
        var variables = new Dictionary<string, string?>(ProcessEnvironment.NameComparer)
        {
            [HomeVariable] = places.Home,
            [NoUpdateVariable] = NoUpdateOn,
            [HomeNameVariable] = places.Os,
            [SearchPath] = BuiltPath([places.PersonsBin, .. places.ToolFolders], parentPath),
        };

        return windows ? WithWindowsNames(variables, places) : WithCache(variables, places);
    }

    private static Dictionary<string, string?> WithWindowsNames(Dictionary<string, string?> variables, KimiPlaces places)
    {
        variables[ProfileVariable] = places.Os;
        variables[DriveVariable] = Drive(places.Os);
        variables[PathVariable] = Rest(places.Os);

        return variables;
    }

    /// <summary>DL11: Kimi extracts about 2 MB of native files on every launch into <c>$XDG_CACHE_HOME</c>, else <c>$HOME/.cache</c> — inside <c>os/</c>, which must stay empty. So the cache folder beside <c>os/</c> is named.</summary>
    private static Dictionary<string, string?> WithCache(Dictionary<string, string?> variables, KimiPlaces places)
    {
        variables[CacheVariable] = places.Cache;

        return variables;
    }

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
    /// FIRST of: the structure (nothing inherited, the minimal names, exactly this platform's names), then each value, then — when the argv
    /// carries them — the two place flags. Each sentence names the value it found. By the machine's own operating system.
    /// </summary>
    public static string Refusal(ProcessRequest request, KimiPlaces places) => Refusal(request, places, OperatingSystem.IsWindows());

    /// <summary>The same for a stated platform (see <see cref="Built(ProcessRequest, KimiPlaces, string, bool)"/>): on Linux and WSL <c>HOME</c> alone is checked, and the cache.</summary>
    public static string Refusal(ProcessRequest request, KimiPlaces places, bool windows) =>
        Checks(request, places, windows)
            .Select(check => check())
            .FirstOrDefault(refusal => refusal.Length > 0) ?? string.Empty;

    private static IEnumerable<Func<string>> Checks(ProcessRequest request, KimiPlaces places, bool windows) =>
    [
        () => Structure(request, windows),
        () => Same(request, HomeVariable, places.Home, "the owned home"),
        .. windows ? WindowsValues(request, places) : UnixValues(request, places),
        () => Pinned(request),
        () => PathBegins(request, places),
        () => FlagIs(request, AcpWords.HomeFlag, places.OwnedRoot, "the owned root"),
        () => FlagIs(request, AcpWords.PersonsBinFlag, places.PersonsBin, "the person's bin"),
    ];

    private static Func<string>[] WindowsValues(ProcessRequest request, KimiPlaces places) =>
    [
        () => Same(request, ProfileVariable, places.Os, "coai's own os/ folder (the OS-home redirect)"),
        () => Same(request, HomeNameVariable, places.Os, "coai's own os/ folder (the OS-home redirect)"),
        () => DrivePath(request, places),
    ];

    private static Func<string>[] UnixValues(ProcessRequest request, KimiPlaces places) =>
    [
        () => Same(request, HomeNameVariable, places.Os, "coai's own os/ folder (the OS-home redirect)"),
        () => Same(request, CacheVariable, places.Cache, "coai's own cache/ folder (where Kimi's native files are extracted, DL11)"),
    ];


    /// <summary>The value after <paramref name="flag"/> in the arguments, or empty when the flag is absent or last.</summary>
    public static string FlagOf(ProcessRequest request, string flag)
    {
        var at = request.Arguments.ToList().IndexOf(flag);

        return at >= 0 && at + 1 < request.Arguments.Count ? request.Arguments[at + 1] : string.Empty;
    }

    private static string ParentPath(ProcessRequest request) =>
        PathOf(request) is { Length: > 0 } own ? own : Environment.GetEnvironmentVariable(SearchPath) ?? string.Empty;

    /// <summary>The <c>PATH</c> a request carries (the built one, for a built request), or empty.</summary>
    public static string PathOf(ProcessRequest request) => Value(request, SearchPath);

    private static string Drive(string os) => Root(os).TrimEnd(Separators);

    private static string Rest(string os) => os[Root(os).TrimEnd(Separators).Length..];

    private static string Root(string os) => Path.GetPathRoot(os) ?? string.Empty;

    /// <summary>The value a request carries for <paramref name="name"/> (names compared as the platform compares them), or empty.</summary>
    private static string Value(ProcessRequest request, string name) =>
        request.Environment.FirstOrDefault(pair => ProcessEnvironment.NameComparer.Equals(pair.Key, name)).Value ?? string.Empty;

    /// <summary>Nothing inherited but the platform's minimal names.</summary>
    private static bool Confined(ProcessRequest request) =>
        !request.InheritsEnvironment && request.Passthrough.SetEquals(ProcessEnvironment.Minimal);

    /// <summary>Exactly this platform's built names — no more, none outside them.</summary>
    private static bool TheBuiltNames(ProcessRequest request, bool windows)
    {
        var names = windows ? WindowsNames : UnixNames;

        return request.Environment.Count == names.Length && request.Environment.Keys.All(name => names.Contains(name, ProcessEnvironment.NameComparer));
    }

    private static string Structure(ProcessRequest request, bool windows) =>
        Confined(request) && TheBuiltNames(request, windows)
            ? string.Empty
            : $"this Kimi launch was not built with coai's own environment (the minimal set plus {HomeVariable}, {NoUpdateVariable}=1, {OsHomeWords(windows)}, and a PATH beginning with your ~/.kimi-code/bin, nothing inherited) and is refused {D19}";

    private static string OsHomeWords(bool windows) => windows
        ? "the four OS-home names pointing into coai's own os/ folder"
        : $"HOME pointing into coai's own os/ folder and {CacheVariable} into its cache/ folder";

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
