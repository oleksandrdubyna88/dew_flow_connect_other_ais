using System.Collections.Immutable;

namespace CoaiMcp.Core.Context;

/// <summary>Where a changed file stood in the reviewers' view — one of four, every file in exactly one.</summary>
public static class ChangeCoverage
{
    public const string Shown = "shown";       // in the reviewers' diff, whole
    public const string Elided = "elided";     // named to the reviewers, not shown: over the shaper's budget (DiffShaper.cs:41-57)
    public const string Excluded = "excluded"; // never reached the reviewers: hidden by the exclusion pathspecs (ContextAssembler.cs:7-30, :294)
    public const string Binary = "binary";     // no line count exists (NumstatReader.cs:66-67 the "-", :143 IsBinary)

    /// <summary>The order cells are written and compared in.</summary>
    public static readonly ImmutableArray<string> All = [Shown, Elided, Excluded, Binary];
}

/// <summary>Why a scope has the cells it has, or none — a closed vocabulary; C2's `size_state`/`delta_state` column.</summary>
public static class ScopeState
{
    public const string Recorded = "recorded";
    public const string FirstRound = "unavailable: first round";
    public const string NoPredecessorSha = "unavailable: no predecessor sha";
    public const string NotAnAncestor = "unavailable: not an ancestor";
    public const string GitFailed = "unavailable: git failed";
    public const string NumstatsDisagree = "unavailable: numstats disagree";
    public const string BaseMoved = "unavailable: base moved";
    public const string BaseUnknown = "unavailable: base unknown";
    public const string NoMergeBase = "unavailable: no merge base";
}

/// <summary>Whether a size was measured by the round that ran, or rebuilt later with today's rules (parent §16 D12).</summary>
public static class SizeOrigin
{
    public const string Live = "live";
    public const string Backfill = "backfill";
}

/// <summary>One (coverage, kind) cell of a scope: how many files, and their lines — null lines for a binary, never 0.</summary>
public sealed record SizeCell(string Coverage, string Kind, int Files, int? Added, int? Removed);

/// <summary>One scope — the whole range or the delta — with its state, the commit it starts at, and its cells.</summary>
public sealed record ScopeSize(string State, string From, IReadOnlyList<SizeCell> Cells)
{
    /// <summary>An unavailable scope: the state says why, the cells are none, `from` is the commit chosen before the failure or empty.</summary>
    public static ScopeSize Unavailable(string state, string from = "") => new(state, from, []);

    /// <summary>A measured zero — two identical heads, or an empty range — which is a `recorded` scope with no cells.</summary>
    public static ScopeSize ObservedZero(string from) => new(ScopeState.Recorded, from, []);
}

/// <summary>What a code round changed, by scope, coverage and kind — the denominator of every later figure (parent §7).</summary>
public sealed record RoundSize(ScopeSize Whole, ScopeSize Delta, string Origin = SizeOrigin.Live);
