namespace CoaiMcp.Core.Catalog;

/// <summary>
/// A catalog row's fast mode (todo/PLAN_fast_mode.md) — the owner's three states of 2026-10-06. <see cref="Off"/> is the
/// default, a row that never set it included: it forces the standard tier, which stops a fast tier the CLI's own
/// configuration may have switched on for every use of it.
/// </summary>
public enum FastMode
{
    /// <summary>Force the standard tier.</summary>
    Off,

    /// <summary>Force the fast tier.</summary>
    On,

    /// <summary>Send nothing — the CLI's own configuration decides.</summary>
    Cli,
}
