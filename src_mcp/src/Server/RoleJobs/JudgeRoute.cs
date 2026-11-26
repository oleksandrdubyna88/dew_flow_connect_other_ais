using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What the judge does to the route the router made (epoch 3 E3 s2 t2): the judge's model list is the <c>important</c> list, and a model the CALLER named that is not on it is refused with a sentence that names
/// <c>COAI_ROLES_MODELS</c> — before any folder is made. The twin of <see cref="CoderRoute"/>, and like it, a rule of the role and not of the router.
/// </summary>
/// <remarks>
/// <para>The router stays pure and role-agnostic: with no model named it already returns the <c>important</c> list for the role <c>important</c> (the judge's <c>RouteAs</c>); with a model named it puts that model first even when it is on no
/// list. For the judge that would run the strongest-model job on a model nobody listed, so it is refused here. An entry that comes from the list is not "named" and is never dropped.</para>
/// <para>The router's own refusals (an empty list, a name no CLI accepts) pass through unchanged.</para>
/// </remarks>
internal static class JudgeRoute
{
    /// <param name="result">The router's answer.</param>
    /// <param name="named">The model the caller named in the call, or empty.</param>
    /// <param name="important">The <c>important</c> list of the roles table.</param>
    public static RouteResult Narrow(RouteResult result, string named, IReadOnlyList<ModelRef> important)
    {
        if (named.Length == 0 || result is not RouteResult.Routed)
        {
            return result;
        }

        return important.Any(model => string.Equals(model.Model, named, StringComparison.OrdinalIgnoreCase))
            ? result
            : new RouteResult.Refused(
                $"the judge runs only on the {RoleRouter.ImportantRole} list ({string.Join(", ", important.Select(model => model.Model))}), and model '{named}' is not on it: "
                + $"name one of those models, or add the model to the '{RoleRouter.ImportantRole}' list in {RoleRouter.ModelsSetting}");
    }
}
