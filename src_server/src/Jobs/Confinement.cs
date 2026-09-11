using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>
/// The one decision a Team-server launch is made from: whether the reviewer was handed everything
/// in its prompt and may reach nothing else.
/// </summary>
/// <remarks>
/// <para><b>One value with two views, rather than two flags.</b> The flags live on two types that
/// never meet: <see cref="ReviewerSettings.Confined"/> is read by the vendor adapter while it
/// composes argv, before a request exists, and <see cref="ProcessRequest.InheritsEnvironment"/> is
/// read by the process launcher after it does. Nothing made them agree, and the code round of epic 1
/// named what that costs (codex, Major, 2026-09-11): a server edit that set one and missed the
/// other would produce a reviewer with an isolated environment and a shell, or the reverse — and
/// both look like a confined launch from every angle except the one that matters, because each half
/// is asserted by a test that cannot see the other.</para>
/// <para>So both are derived here, from this one field, and <see cref="ReviewLauncher"/> builds
/// every invocation through them in one step. What a test can observe is that the two views move
/// together when the value moves (<c>ConfinementTests</c>) and that the request the launcher hands
/// over carries both (<c>ReviewLauncherTests</c>); that there is no second road to a request is a
/// property of the launcher's shape, which is the part a type guarantees and a test cannot.</para>
/// <para>The value is a parameter rather than a constant for the same reason
/// <c>ProcessEnvironment.ForPlatform</c> takes its platform as one: a derivation that can only ever
/// be asked one question cannot be shown to derive anything. There is no unconfined launch on this
/// server — <see cref="OfEveryJob"/> is the only instance production code reads.</para>
/// </remarks>
public sealed record Confinement(bool Confined)
{
    /// <summary>What every job on this server gets. There is no unconfined launch here.</summary>
    public static Confinement OfEveryJob { get; } = new(Confined: true);

    /// <summary>The adapter's half: the tools it asks the CLI to deny.</summary>
    public ReviewerSettings Apply(ReviewerSettings settings) => settings with { Confined = Confined };

    /// <summary>The launcher's half: whether the child starts from this process's whole environment.</summary>
    public ProcessRequest Apply(ProcessRequest request) => request with { InheritsEnvironment = !Confined };
}
