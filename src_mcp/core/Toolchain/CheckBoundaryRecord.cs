namespace CoaiMcp.Core.Toolchain;

/// <summary>One item the preflight of the check boundary asked, what it was, and what the OS answered.</summary>
/// <param name="Item">The item's name — a fixed word, the same on every run.</param>
/// <param name="Held">True when the answer was the right one; one false row refuses the job.</param>
/// <param name="Detail">One sentence: what was seen. It never holds a secret.</param>
public sealed record PreflightRow(string Item, bool Held, string Detail);

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
