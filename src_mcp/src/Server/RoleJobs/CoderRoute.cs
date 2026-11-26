using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What the coder does to the route the router made (epoch 3 E2 s3 t2): the coder's write launch is measured and built for the claude CLI alone (<c>CoderLaunches.ClaudeOnly</c>), so an entry of another vendor is dropped
/// from a coder route, and a route that has no claude model left is refused — naming <c>COAI_ROLES_MODELS</c>, where the lists are written — before any folder is made.
/// </summary>
/// <remarks>
/// <para>The router stays pure and role-agnostic; this is the coder's own rule, applied where the plan of a coder job is made (<see cref="RoleRouting.Plan"/>).</para>
/// <para><b>A model the caller named is never dropped (epoch 3 E2 s3 t3).</b> The caller asked for that model in the call itself (<c>model</c> of <c>start_role</c>); answering with a route that quietly runs another one would bill
/// the wrong model and hide the refusal. When the model the caller named is placed on a vendor that is not claude, the start is refused with a sentence that names the model and the vendor. Entries that come from the
/// bands or from the lists are not the caller's word and are still dropped.</para>
/// </remarks>
internal static class CoderRoute
{
    /// <summary>The vendor the coder runs on.</summary>
    public const string Vendor = "claude";

    /// <param name="result">The router's answer.</param>
    /// <param name="named">The model the caller named in the call, or empty.</param>
    public static RouteResult Narrow(RouteResult result, string named = "")
    {
        if (result is not RouteResult.Routed routed)
        {
            return result;
        }

        if (named.Length > 0 && routed.Models.FirstOrDefault(model => string.Equals(model.Model, named, StringComparison.OrdinalIgnoreCase)) is { } asked && !IsClaude(asked))
        {
            return new RouteResult.Refused($"the coder is claude only in this epic; {named} is {asked.Vendor}");
        }

        var dropped = routed.Models.Where(model => !IsClaude(model)).ToList();
        var kept = routed.Models.Where(IsClaude).ToList();
        if (kept.Count == 0)
        {
            return new RouteResult.Refused(
                $"the coder runs only on claude models in this epoch, and its route has none ({string.Join(", ", RoleRoute.Format(dropped))}): "
                + $"put a claude model into the '{RoleRouter.CoderRole}' list (and the '{RoleRouter.ImportantRole}' list for an important task) in {RoleRouter.ModelsSetting}, and into the complexity bands in {RoleRouter.BandsSetting}");
        }

        return dropped.Count == 0
            ? routed
            : new RouteResult.Routed(kept, $"{routed.Why}; dropped because the coder is claude only: {string.Join(", ", RoleRoute.Format(dropped))}");
    }

    private static bool IsClaude(ModelRef model) => string.Equals(model.Vendor, Vendor, StringComparison.OrdinalIgnoreCase);
}
