using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What the coder does to the route the router made (epoch 3 E2 s3 t2): the coder's write launch is measured and built for the claude CLI alone (<c>CoderLaunches.ClaudeOnly</c>), so an entry of another vendor is dropped
/// from a coder route, and a route that has no claude model left is refused — naming <c>COAI_ROLES_MODELS</c>, where the lists are written — before any folder is made.
/// </summary>
/// <remarks>
/// The router stays pure and role-agnostic; this is the coder's own rule, applied where the plan of a coder job is made (<see cref="RoleRouting.Plan"/>). A model only the caller named (<c>model</c> of <c>start_role</c>) is dropped
/// like any other entry when it is not claude: the caller asked for the model, not for a launch the coder cannot make.
/// </remarks>
internal static class CoderRoute
{
    /// <summary>The vendor the coder runs on.</summary>
    public const string Vendor = "claude";

    public static RouteResult Narrow(RouteResult result)
    {
        if (result is not RouteResult.Routed routed)
        {
            return result;
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
