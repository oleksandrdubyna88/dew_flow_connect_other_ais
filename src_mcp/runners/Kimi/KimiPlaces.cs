using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// Where everything of a Kimi launch is — the person's folders and coai's owned ones — resolved ONCE, in the unredirected
/// parent, and carried down (D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; E4.2 decision 4 and brief section 2).
/// </summary>
/// <remarks>
/// <para><b>Why once, and why in the parent.</b> The shim's own environment is the redirected one — inside it <c>USERPROFILE</c>
/// IS the owned <c>os/</c> folder — so a process that asked "where is the person's <c>~/.kimi-code</c>" there would be told
/// where coai's empty folder is. The runtime (E4.4) calls <see cref="Resolve"/> with the REAL profile and this process's
/// environment; the shim (E4.3) calls <see cref="ForShim"/>, which reads NO environment, with the two paths the runtime passed on
/// its argv (<c>--home</c>, <c>--persons-bin</c>). <see cref="HostKind"/> is a parameter everywhere; <c>HostKinds.Current</c> is
/// the runtime's call site's business.</para>
/// <para>Kimi's own variables are honoured: <c>KIMI_CODE_HOME</c> names the person's data folder (the one holding
/// <c>credentials</c> and <c>config.toml</c>) and <c>KIMI_INSTALL_DIR</c> the installer's, whose <c>bin</c> holds <c>kimi</c>.</para>
/// </remarks>
/// <param name="OwnedRoot">The coai-owned root: holds <c>home/</c> (the owned <c>KIMI_CODE_HOME</c>) and <c>os/</c> (the empty OS home).</param>
/// <param name="PersonsHome">The person's data folder; empty for the shim, which learns the sign-in folder from the owned junction's own reparse data.</param>
/// <param name="PersonsBin">The folder the person's tools live in; the built <c>PATH</c> begins with it.</param>
/// <param name="ToolFolders">The other folders <c>rg</c> and <c>fd</c> were found in (<see cref="KimiTools.FoldersFor"/>).</param>
/// <param name="ToolsRefusal">Why <c>rg</c> and <c>fd</c> were not both found, or empty.</param>
/// <param name="Executable">The absolute <c>kimi</c> — or the bare name, which fails the launch with the install cure; empty for the shim.</param>
public sealed record KimiPlaces(string OwnedRoot, string PersonsHome, string PersonsBin, IReadOnlyList<string> ToolFolders, string ToolsRefusal, string Executable)
{
    private const string Bare = "kimi";

    /// <summary>The owned <c>KIMI_CODE_HOME</c>.</summary>
    public string Home => Path.Combine(OwnedRoot, "home");

    /// <summary>The empty OS home the redirected <c>USERPROFILE</c>/<c>HOME</c> point at.</summary>
    public string Os => Path.Combine(OwnedRoot, "os");

    /// <summary>
    /// The cache folder <c>XDG_CACHE_HOME</c> points at on Linux and WSL (DL11 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): beside <c>os/</c> and NOT inside <c>home/</c> (whose shape <see cref="KimiHome"/> judges), so Kimi's
    /// native-file extraction (about 2 MB per launch, measured by L-K3) lands here and <c>os/</c> stays empty. Made <c>0700</c> with the home, deleted with the owned root.
    /// </summary>
    public string Cache => Path.Combine(OwnedRoot, "cache");

    /// <summary>The junction to the person's <c>credentials</c>, inside the owned home.</summary>
    public string Link => Path.Combine(Home, "credentials");

    /// <summary>The one <c>config.toml</c> coai writes.</summary>
    public string Config => Path.Combine(Home, "config.toml");

    /// <summary>The person's <c>credentials</c> folder — the junction's target; empty when the person's home is not known (the shim).</summary>
    public string Credentials => PersonsHome.Length == 0 ? string.Empty : Path.Combine(PersonsHome, "credentials");

    /// <summary>The person's own <c>config.toml</c>, read for the OAuth slot and the model block; empty for the shim.</summary>
    public string PersonsConfig => PersonsHome.Length == 0 ? string.Empty : Path.Combine(PersonsHome, "config.toml");

    /// <summary>The places for a launch of the real <c>kimi</c>, from the parent's own environment and the REAL profile.</summary>
    public static KimiPlaces Resolve(string ownedRoot, HostKind host, Func<string, string?> env, string userProfile, Func<string, bool> fileExists)
    {
        var windows = host == HostKind.Windows;
        var installer = Join(windows, userProfile, ".kimi-code");
        var home = NonEmpty(env("KIMI_CODE_HOME"), installer);
        var bin = Join(windows, NonEmpty(env("KIMI_INSTALL_DIR"), installer), "bin");
        var localBin = windows ? string.Empty : Join(false, Join(false, userProfile, ".local"), "bin");

        return new KimiPlaces(
            ownedRoot,
            home,
            bin,
            KimiTools.FoldersFor(windows, bin, env("PATH"), env("PATHEXT"), fileExists, localBin),
            KimiTools.Refusal(windows, bin, env("PATH"), env("PATHEXT"), fileExists, localBin),
            ExecutableOf(host, bin, userProfile, env("PATH"), fileExists));
    }

    /// <summary>The places the SHIM knows: the owned root and the person's bin it was handed — nothing read from any environment.</summary>
    public static KimiPlaces ForShim(string ownedRoot, string personsBin) =>
        new(ownedRoot, string.Empty, personsBin, [], string.Empty, string.Empty);

    /// <summary>The installed <c>kimi</c> by absolute path: the person's bin, then the installer's default, then wherever <c>PATH</c> finds it — else the bare name.</summary>
    private static string ExecutableOf(HostKind host, string bin, string userProfile, string? path, Func<string, bool> fileExists)
    {
        var windows = host == HostKind.Windows;
        var inBin = InstallersExecutable(host, bin);
        Func<string>[] ways =
        [
            () => fileExists(inBin) ? inBin : string.Empty,
            () => KimiAcpProfile.InstalledExecutableFor(host, userProfile, path, fileExists),
            () => ExecutableResolver.Resolve(Bare, windows, path, fileExists),
        ];

        return ways.Select(way => way()).FirstOrDefault(found => found.Length > 0 && found != Bare) ?? Bare;
    }

    private static string NonEmpty(string? value, string fallback) => value is { Length: > 0 } ? value : fallback;

    /// <summary>Where Moonshot's installer puts <c>kimi</c> inside the person's bin: <c>kimi.exe</c> on Windows, <c>kimi</c> on Linux and WSL (DL5), and nowhere on a host nothing was measured on.</summary>
    private static string InstallersExecutable(HostKind host, string bin) => host switch
    {
        HostKind.Windows => Join(true, bin, "kimi.exe"),
        HostKind.Linux or HostKind.Wsl => Join(false, bin, Bare),
        _ => string.Empty,
    };

    /// <summary>
    /// A path joined with the HOST's separator — backslashes for Windows, <c>/</c> for everything else — whatever machine is asking, like <see cref="KimiAcpProfile.InstalledExecutableFor"/>:
    /// never <c>Path.Combine</c>, which spells the RUNNING operating system's, so a Linux path is judged the same on a Windows test host.
    /// </summary>
    private static string Join(bool windows, string folder, string name) =>
        windows ? $"{folder.TrimEnd('\\', '/')}\\{name}" : folder.Length == 0 ? name : $"{folder.TrimEnd('/')}/{name}";
}
