using CoaiMcp.Core.Api;

namespace CoaiMcp.Server;

/// <summary>
/// An <c>api</c> row through its module's eyes — the one computation the roster (to launch) and
/// <c>providers</c> (to report) share: which module the row resolves to, what it will run with, and
/// why it cannot run if its own settings are refused.
/// </summary>
/// <remarks>
/// A row whose dialect this build does not know resolves to the generic module HERE so its settings
/// can still be reported; the shim then refuses the unknown dialect with 65 at launch, as it always has.
/// </remarks>
/// <param name="Note">Why the row's named module was set aside for its model (<see cref="ApiVendors.SetAside"/>), and why the
/// environment's effort is sent without being one of the module's levels (<see cref="ApiEffective.Unlisted"/>) — or empty.</param>
internal sealed record ApiRowView(IApiVendor Module, ApiEffective Effective, string Refusal, string Note = "")
{
    public static ApiRowView Of(ProviderSettings provider, ApiOverrides overrides)
    {
        var module = ApiVendors.Resolve(provider.Dialect, provider.Model) ?? OpenAiCompatibleVendor.Generic;

        return new ApiRowView(
            module,
            ApiEffective.Of(module, provider.Api, overrides),
            module.Refusal(provider.Api),
            string.Join("; ", ((string[])[
                ApiVendors.SetAside(provider.Dialect, provider.Model),
                ApiEffective.Unlisted(module, provider.Api, overrides)]).Where(note => note.Length > 0)));
    }

    /// <summary>What <c>providers</c> prints for the row: names and values, never a key.</summary>
    public ApiRowReport Report() =>
        new(Module.Name, Module.MeasuredModel, Module.PriceRoute, Module.Capabilities, Module.Defaults, Effective, Refusal, Note);
}
