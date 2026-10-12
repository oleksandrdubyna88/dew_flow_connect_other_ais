namespace CoaiMcp.Runners.Acp;

/// <summary>
/// One vendor's ACP agent: how it is started, which config option carries the model, which names its
/// shim refuses to read, and which of its failure sentences coai can name.
/// </summary>
/// <remarks>
/// <para>One module per vendor behind one interface (the owner's rule, D1 of
/// <c>PLAN_vibe_and_kimi_runtimes.md</c>): the client (<see cref="AcpClient"/>) speaks the Agent Client
/// Protocol and knows no vendor; everything a vendor decides is a member here, so a second ACP agent is a
/// second class and never an <c>if</c> on a name. Today the only profile is <see cref="KimiAcpProfile"/>
/// (D15).</para>
/// </remarks>
public interface IAcpProfile
{
    /// <summary>The name a launch asks for it by — <c>--profile kimi</c>.</summary>
    string Name { get; }

    /// <summary>The executable to start when the launch names none — a bare command for the PATH, or the installer's own path.</summary>
    string DefaultExecutable { get; }

    /// <summary>The arguments that put the CLI into ACP mode on stdio.</summary>
    IReadOnlyList<string> Arguments { get; }

    /// <summary>The <c>session/set_config_option</c> id that selects the model.</summary>
    string ModelOptionId { get; }

    /// <summary>
    /// The names the read boundary refuses at every depth — the vendor's own project-instruction and
    /// configuration paths, as relative fragments (<c>AGENTS.md</c>, <c>.kimi-code</c>, <c>.agents/skills</c>).
    /// </summary>
    IReadOnlyList<string> ExcludedNames { get; }

    /// <summary>The failure sentences this vendor is known to write, each with its name and its cure.</summary>
    IReadOnlyList<AcpKnownFailure> KnownFailures { get; }
}

/// <summary>What a known failure means for the launch: the account cannot have this (a sign-in or a plan), or it is spent.</summary>
public enum AcpFailureKind
{
    /// <summary>The agent will not serve this account or model — not signed in, or a plan without the model.</summary>
    Refused,

    /// <summary>An allowance ran out — a billing or quota refusal; waiting does not clear it.</summary>
    Spent,
}

/// <summary>One sentence a vendor writes, the name coai records it under, and what a person does about it.</summary>
/// <param name="Phrase">The substring that identifies it, matched ordinally and case-insensitively.</param>
/// <param name="Name">The diagnosis name a reader of the trace sees — <c>subscription_model</c>, <c>not_signed_in</c>.</param>
/// <param name="Cure">The sentence that tells a person what to do.</param>
/// <param name="Kind">Which exit the shim answers with.</param>
public sealed record AcpKnownFailure(string Phrase, string Name, string Cure, AcpFailureKind Kind);

/// <summary>A known failure found in what the agent said — or <see cref="None"/>.</summary>
public sealed record AcpDiagnosis(string Name, string Cure, AcpFailureKind Kind)
{
    /// <summary>Nothing recognised: the agent's own words stand.</summary>
    public static readonly AcpDiagnosis None = new(string.Empty, string.Empty, AcpFailureKind.Refused);

    /// <summary>True when a phrase matched.</summary>
    public bool Known => Name.Length > 0;

    /// <summary>
    /// The FIRST known failure whose phrase is in <paramref name="text"/>, in the profile's order — so a
    /// profile lists the specific sentence before the general one it contains (the subscription 401 begins
    /// with the words of the sign-in one).
    /// </summary>
    public static AcpDiagnosis Of(string text, IReadOnlyList<AcpKnownFailure> known) =>
        known.FirstOrDefault(failure => text.Contains(failure.Phrase, StringComparison.OrdinalIgnoreCase)) is { } hit
            ? new AcpDiagnosis(hit.Name, hit.Cure, hit.Kind)
            : None;
}
