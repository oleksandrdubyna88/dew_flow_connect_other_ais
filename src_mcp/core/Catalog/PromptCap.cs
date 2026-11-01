namespace CoaiMcp.Core.Catalog;

/// <summary>
/// A row's prompt cap — the most its reviewer is sent in a first prompt, as the operator wrote it in whole KiB
/// (<c>maxPromptKib</c>; research/PLAN_a_reviewer_is_not_sent_more_than_it_can_read.md D1, D2). An operator's number, never a
/// model capability the product guesses: there is no default, and <see cref="None"/> is "no cap", as it has always been.
/// </summary>
/// <remarks>
/// <para>One type owns the three facts that must agree: the range (1 to <see cref="CatalogLimits.MaxPromptKib"/>, anything else
/// is no cap), the unit (exactly <c>KiB × 1024</c> bytes) and the boundary (a prompt of exactly the cap is read; one byte
/// more is not). A bare <c>int</c> carried all three as conventions of whoever read it.</para>
/// <para>Parsed by <see cref="OfKib"/>, which never throws: a value out of range is an expected answer, and it is no cap.</para>
/// </remarks>
public readonly record struct PromptCap
{
    /// <summary>No cap: every prompt is admitted.</summary>
    public static readonly PromptCap None;

    private PromptCap(int kib) => Kib = kib;

    /// <summary>The cap in whole KiB as the operator wrote it, or 0 for <see cref="None"/>.</summary>
    public int Kib { get; }

    /// <summary>Whether the row has a cap at all.</summary>
    public bool IsSet => Kib > 0;

    /// <summary>The cap in bytes — exactly <see cref="Kib"/> × 1024 — or 0 for <see cref="None"/>.</summary>
    public int Bytes => Kib * 1024;

    /// <summary>A cap of this many KiB, or <see cref="None"/> when it is not a whole KiB from 1 to <see cref="CatalogLimits.MaxPromptKib"/>.</summary>
    public static PromptCap OfKib(long kib) =>
        kib is > 0 and <= CatalogLimits.MaxPromptKib ? new PromptCap((int)kib) : None;

    /// <summary>Whether a first prompt of this many UTF-8 bytes may be sent — always for <see cref="None"/>; the cap is inclusive.</summary>
    public bool Admits(int promptBytes) => !IsSet || promptBytes <= Bytes;
}
