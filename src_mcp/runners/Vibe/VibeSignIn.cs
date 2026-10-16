using CoaiMcp.Runners.Platform;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// Where the person's Vibe sign-in file is (DL2 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): the file the launch control LINKS into a
/// coai-owned Vibe home, so Vibe finds its own sign-in without coai ever reading, holding or copying the key.
/// </summary>
/// <remarks>
/// Resolved ONCE, where the adapter is made (as <see cref="VibePathBudget"/> is), from coai's OWN unredirected environment:
/// <c>VIBE_HOME</c> when set, else <c>&lt;profile&gt;/.vibe</c> (the <c>KIMI_CODE_HOME</c> rule of D19); the file is exactly
/// <c>&lt;that&gt;/.env</c>, absolute. Empty on Windows, where the sign-in is in the OS keyring and no link is ever made or admitted.
/// </remarks>
/// <param name="PersonsEnvFile">The absolute path of the person's sign-in file, or empty when no link is made on this host.</param>
public sealed record VibeSignIn(string PersonsEnvFile)
{
    /// <summary>The one name a link may have in an owned home: the file Vibe reads its sign-in from (<c>VIBE_HOME/.env</c>).</summary>
    public const string LinkName = ".env";

    /// <summary>No link: Windows, or no profile to name a Vibe home in.</summary>
    public static VibeSignIn None { get; } = new(string.Empty);

    public bool Links => PersonsEnvFile.Length > 0;

    /// <summary>The disk steps making the link takes — the real ones; a test hands in stand-ins for a host that cannot make a link or a filesystem that fails.</summary>
    internal SignInSteps Steps { get; init; } = SignInSteps.Real;

    /// <summary>Pure: the host, a reader of coai's own environment, and the profile folder.</summary>
    public static VibeSignIn For(HostKind host, Func<string, string?> environment, string userProfile) =>
        host == HostKind.Windows || userProfile.Length == 0
            ? None
            : new(Path.GetFullPath(Path.Combine(PersonsHome(environment(VibeLaunch.HomeVariable), userProfile), LinkName)));

    /// <summary>The person's Vibe home: <c>VIBE_HOME</c> when it names one, else <c>~/.vibe</c>.</summary>
    private static string PersonsHome(string? fromEnvironment, string userProfile) =>
        string.IsNullOrWhiteSpace(fromEnvironment) ? Path.Combine(userProfile, ".vibe") : fromEnvironment;

    /// <summary>This process's: from its own environment and profile folder, on this host.</summary>
    public static VibeSignIn ForThisProcess(HostKind host) =>
        For(host, Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>The one link this sign-in admits in an owned home: <c>.env</c> at the top level, to <see cref="PersonsEnvFile"/>.</summary>
    public AdmittedLink Admitted => Links ? new AdmittedLink(LinkName, PersonsEnvFile) : AdmittedLink.None;
}

/// <summary>A link an owned Vibe home may hold: its top-level name and the exact target spelling. <see cref="None"/> admits no link.</summary>
public sealed record AdmittedLink(string Name, string Target)
{
    public static AdmittedLink None { get; } = new(string.Empty, string.Empty);

    public bool Admits => Name.Length > 0;
}
