using System.Globalization;

namespace CoaiMcp.Core.Findings;

/// <summary>Dollars per million tokens: fresh input, cached input, output. Zero means "not known", never "free".</summary>
public sealed record TokenRates(double In, double Cached, double Out)
{
    public static readonly TokenRates None = new(0, 0, 0);

    /// <summary>Whether anything is priced at all — an input or an output rate.</summary>
    public bool IsSet => In > 0 || Out > 0;

    /// <summary>A rate as a number that can be multiplied: a negative, NaN or infinite one is no rate.</summary>
    public static double Clean(double rate) => double.IsFinite(rate) && rate > 0 ? rate : 0;
}

/// <summary>
/// What a metered vendor row charges — the rates, and an optional long-context tier.
/// </summary>
/// <remarks>
/// <para><b>Why the server prices anything at all</b> (PLAN_feature_review.md S3.7). An <c>api</c> reviewer
/// is billed per token by the key's own vendor, and an OpenAI-compatible response carries tokens and no
/// money. The rates cross with the vendor row from the panel, which takes them from the lookup it already
/// had — a rate somebody typed wins over a looked-up one — so this record holds numbers and no table.</para>
/// <para><b>The tier</b>: xAI doubles every rate once a request's prompt reaches 200K tokens, and LiteLLM
/// lists it as <c>*_above_200k_tokens</c>. <see cref="TierFromTokens"/> of zero means there is none.
/// The threshold is read against the TURN's input — one request, one bill — which is why a feature
/// reviewer's conversation is priced turn by turn, never on its sum.</para>
/// </remarks>
public sealed record TokenPrice(TokenRates Rates, long TierFromTokens, TokenRates Tier)
{
    public static readonly TokenPrice None = new(TokenRates.None, 0, TokenRates.None);

    /// <summary>Whether this row has a price. An unpriced metered row is "no price set", never $0.</summary>
    public bool IsSet => Rates.IsSet;

    /// <summary>
    /// <c>(in − cached)·pIn + cached·pCached + out·pOut</c>, per million, at the tier's rates once the
    /// turn's input reaches the tier — or null when there is no price.
    /// </summary>
    /// <remarks>
    /// Cached tokens are a SUBSET of the input (<see cref="Usage.TokensCached"/>), so they are taken out of
    /// the fresh input before it is priced, and a count that claims more cached than input is clamped to the
    /// input. A row with no cached rate prices them at the input rate: the list simply did not say, and
    /// charging them nothing would under-report the one number this exists to get right.
    /// </remarks>
    public double? CostOf(Usage usage)
    {
        if (!IsSet)
        {
            return null;
        }

        var rates = TierFromTokens > 0 && Tier.IsSet && usage.TokensIn >= TierFromTokens ? Tier : Rates;
        // The fresh input is never negative: a vendor reporting more cached tokens than prompt tokens
        // (epic 3's code round, #5) has every prompt token priced at the cached rate and nothing refunded.
        var cached = Math.Clamp(usage.TokensCached, 0, Math.Max(usage.TokensIn, 0));
        var fresh = Math.Max(0, usage.TokensIn - cached);
        var cachedRate = rates.Cached > 0 ? rates.Cached : rates.In;
        var dollars = (fresh * rates.In + cached * cachedRate + usage.TokensOut * rates.Out) / 1_000_000d;

        return Math.Round(dollars, 6);
    }
}

/// <summary>How a cost is written where a person reads it: a figure, or why there is none.</summary>
public static class CostText
{
    /// <summary>What a metered run with no rate says instead of a figure — never <c>$0</c>.</summary>
    public const string NoPriceSet = "no price set";

    /// <summary>
    /// <c>", $0.0123"</c>; <c>", no price set"</c> when a metered run had no rate; or
    /// <paramref name="nothing"/> when nobody priced anything and nothing was metered.
    /// </summary>
    public static string Of(double? costUsd, bool noPriceSet, string nothing = "") => (costUsd, noPriceSet) switch
    {
        ({ } usd, false) => string.Create(CultureInfo.InvariantCulture, $", ${usd:0.0000}"),
        ({ } usd, true) => string.Create(CultureInfo.InvariantCulture, $", ${usd:0.0000} + a reviewer with {NoPriceSet}"),
        (null, true) => $", {NoPriceSet}",
        _ => nothing,
    };
}
