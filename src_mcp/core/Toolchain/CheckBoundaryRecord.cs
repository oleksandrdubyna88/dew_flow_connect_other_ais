namespace CoaiMcp.Core.Toolchain;

/// <summary>One item the preflight of the check boundary asked, what it was, and what the OS answered.</summary>
/// <param name="Item">The item's name — a fixed word, the same on every run.</param>
/// <param name="Held">True when the answer was the right one; one false row refuses the job.</param>
/// <param name="Detail">One sentence: what was seen. It never holds a secret.</param>
public sealed record PreflightRow(string Item, bool Held, string Detail)
{
    /// <summary>
    /// True for a RED row that is not a fault of the boundary but a model that cannot be used here now (epoch 3 E3 s2 t5; <c>SPEC_CHANGES.md</c> Q2): the tool list of a codex coder could not be read, in both reads. When EVERY red row of a preflight is one of these, the refusal is <c>model-unavailable</c> and fails over;
    /// with any other row red too, the boundary row wins and the job ends <c>coder-boundary</c>.
    /// </summary>
    public bool Unavailable { get; init; }

    /// <summary>For a held row that reads a tool list: the known tools this model does not have (coai's own strings, so they can be shown). Empty for every other row.</summary>
    public IReadOnlyList<string> Absent { get; init; } = [];

    /// <summary>For the red row of a tool list with names outside the known set: those names as a person may read them (at most 5, each only when it is 1 to 64 safe characters and the shared redactor leaves it as it is; the rest as a name that cannot be shown, with its length). Empty for every other row.</summary>
    public string Tools { get; init; } = string.Empty;

    /// <summary>Why the person is told by the notice <c>coder-tool-unknown</c> for a red row of the tool inventory: <c>unknown-tool</c> or <c>inventory-not-whole</c> (then no name is shown). Empty for every other row.</summary>
    public string ToolReason { get; init; } = string.Empty;
}

/// <summary>
/// What a check step was run inside, written down: the boundary, every property it was started with, the network state and what the preflight saw — so a reader of a result never has to guess whether "green" was bounded.
/// </summary>
/// <param name="Boundary">The kind: <see cref="UnitBoundary"/>, or <see cref="NoBoundary"/> for a run that was not a coder job's.</param>
/// <param name="Properties">The <c>systemd-run -p</c> properties, one <c>Name=Value</c> each, in the order they were given.</param>
/// <param name="Network"><c>none</c> (a private network namespace with only a loopback device) or <c>host</c>.</param>
/// <param name="NetworkReason">Why the step has that network state — the measured reason when it is <c>host</c>.</param>
/// <param name="Preflight">The preflight rows that were answered before the job; empty for a run with no boundary.</param>
public sealed record BoundaryRecord(string Boundary, IReadOnlyList<string> Properties, string Network, string NetworkReason, IReadOnlyList<PreflightRow> Preflight)
{
    /// <summary>A named <c>systemd-run --user</c> unit with the property set of <c>v2/spikes/RESULTS_coder_write_boundary_judgement.md</c> § 2.</summary>
    public const string UnitBoundary = "systemd-run-user-unit";

    /// <summary>No OS boundary: the run was not a coder job's.</summary>
    public const string NoBoundary = "none";

    /// <summary>The record of a run that was not bounded. Not a default for a coder job: a coder job that has no boundary is refused.</summary>
    public static BoundaryRecord None { get; } = new(NoBoundary, [], "host", "no boundary was used for this run", []);

    /// <summary>Whether the step ran inside an OS boundary.</summary>
    public bool Bounded => Boundary != NoBoundary;
}
