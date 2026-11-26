using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What the coder does to the route the router made (epoch 3 E2 s3 t2, E3 s7 t4): a coder is launched only on a vendor whose write launch is SHIPPED (<see cref="CoderVendorSet"/>: claude always, codex and agy only with a verdict of ship AND a measured release in
/// <c>shared/coder-boundary-releases.json</c>), so an entry of another vendor is dropped from a coder route with the reason, and a route that has none left is refused — naming <c>COAI_ROLES_MODELS</c>, where the lists are written — before any folder is made.
/// </summary>
/// <remarks>
/// <para>The router stays pure and role-agnostic; this is the coder's own rule, applied where the plan of a coder job is made (<see cref="RoleRouting.Plan"/>). This class decides nothing about a vendor itself: it asks the set.</para>
/// <para><b>A model the caller named is never dropped (epoch 3 E2 s3 t3).</b> The caller asked for that model in the call itself (<c>model</c> of <c>start_role</c>); answering with a route that quietly runs another one would bill
/// the wrong model and hide the refusal. When the model the caller named is placed on a vendor that is not shipped, the start is refused with a sentence that names the model and the vendor. Entries that come from the bands or from the lists are not the caller's word and are still dropped.</para>
/// </remarks>
internal static class CoderRoute
{
    /// <param name="result">The router's answer.</param>
    /// <param name="named">The model the caller named in the call, or empty.</param>
    /// <param name="vendors">The vendors a coder can be launched on; the product's set unless a test injects another.</param>
    public static RouteResult Narrow(RouteResult result, string named = "", CoderVendorSet? vendors = null)
    {
        if (result is not RouteResult.Routed routed)
        {
            return result;
        }

        var set = vendors ?? CoderVendors.Shipped;
        var shipped = set.Launchable;
        if (named.Length > 0 && routed.Models.FirstOrDefault(model => string.Equals(model.Model, named, StringComparison.OrdinalIgnoreCase)) is { } asked && !Ships(set, asked))
        {
            return new RouteResult.Refused($"the coder cannot run {named} on {asked.Vendor}: {WhyNot(set, asked)}");
        }

        var dropped = routed.Models.Where(model => !Ships(set, model)).ToList();
        var kept = routed.Models.Where(model => Ships(set, model)).ToList();
        if (kept.Count == 0)
        {
            return new RouteResult.Refused(
                $"the coder's route has no model on a vendor that is shipped for the coder ({string.Join(", ", RoleRoute.Format(dropped))}); the shipped coder vendors are {string.Join(", ", shipped)}: "
                + $"put a model of one of them into the '{RoleRouter.CoderRole}' list (and the '{RoleRouter.ImportantRole}' list for an important task) in {RoleRouter.ModelsSetting}, and into the complexity bands in {RoleRouter.BandsSetting}");
        }

        return dropped.Count == 0
            ? routed
            : new RouteResult.Routed(kept, $"{routed.Why}; dropped because the coder is not shipped for their vendor (shipped: {string.Join(", ", shipped)}): {string.Join(", ", RoleRoute.Format(dropped))}");
    }

    private static string WhyNot(CoderVendorSet set, ModelRef model) => set.Resolve(Vendor(model)) is CoderVendorResolution.Refused refusal ? refusal.Sentence : "its vendor is not shipped";

    private static string Vendor(ModelRef model) => model.Vendor.ToLowerInvariant();

    private static bool Ships(CoderVendorSet set, ModelRef model) => set.Resolve(Vendor(model)) is CoderVendorResolution.Launchable;
}
