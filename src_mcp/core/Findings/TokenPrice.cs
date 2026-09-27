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
        var cached = Math.Clamp(usage.TokensCached, 0, Math.Max(usage.TokensIn, 0));
        var cachedRate = rates.Cached > 0 ? rates.Cached : rates.In;
        var dollars = ((usage.TokensIn - cached) * rates.In + cached * cachedRate + usage.TokensOut * rates.Out) / 1_000_000d;

        return Math.Round(dollars, 6);
    }

    /// <summary>The argv flags that carry this price to <c>--ask-api</c> — none at all when there is no price.</summary>
    /// <remarks>A price is not a secret, so it may ride on a command line; the key never does.</remarks>
    public IReadOnlyList<string> AsFlags()
    {
        if (!IsSet)
        {
            return [];
        }

        var flags = new List<string> { "--price-in", Text(Rates.In), "--price-cached", Text(Rates.Cached), "--price-out", Text(Rates.Out) };
        if (TierFromTokens > 0 && Tier.IsSet)
        {
            flags.AddRange(["--tier-from", TierFromTokens.ToString(CultureInfo.InvariantCulture),
                "--tier-in", Text(Tier.In), "--tier-cached", Text(Tier.Cached), "--tier-out", Text(Tier.Out)]);
        }

        return flags;
    }

    /// <summary>The price the flags of <see cref="AsFlags"/> describe; <see cref="None"/> when they describe none.</summary>
    public static TokenPrice FromFlags(IReadOnlyDictionary<string, string> flags)
    {
        var rates = new TokenRates(Number(flags, "--price-in"), Number(flags, "--price-cached"), Number(flags, "--price-out"));
        var from = long.TryParse(flags.GetValueOrDefault("--tier-from", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 0;
        var tier = new TokenRates(Number(flags, "--tier-in"), Number(flags, "--tier-cached"), Number(flags, "--tier-out"));

        return rates.IsSet ? new TokenPrice(rates, from, from > 0 ? tier : TokenRates.None) : None;
    }

    private static double Number(IReadOnlyDictionary<string, string> flags, string name) =>
        double.TryParse(flags.GetValueOrDefault(name, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? TokenRates.Clean(value)
            : 0;

    private static string Text(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
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
