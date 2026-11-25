using System.Text;
using CoaiMcp.Core.Roles;
using CoaiMcp.Core.Work;
using CoaiMcp.Roles;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// How a route is written in <c>job.json</c> and in the answer of <c>start_role</c>: one string per model, <c>vendor/model</c> (<c>claude/fable</c>, <c>codex/astra</c>) — the vendor first, because a person reading a route
/// wants to know whose CLI the work goes to. The vendor never holds a slash, so the first one splits it.
/// </summary>
internal static class RoleRoute
{
    public static string Format(ModelRef model) => $"{model.Vendor}/{model.Model}";

    public static IReadOnlyList<string> Format(IEnumerable<ModelRef> models) => [.. models.Select(Format)];

    /// <summary>The model a route entry names, or null for an entry that is not <c>vendor/model</c> (a hand-edited file).</summary>
    public static ModelRef? Parse(string entry)
    {
        var slash = entry.IndexOf('/', StringComparison.Ordinal);

        return slash > 0 && slash < entry.Length - 1 ? new ModelRef(entry[(slash + 1)..], entry[..slash]) : null;
    }
}

/// <summary>
/// What the router needs to know of the task a job is for, read from the task file at the START of the job — and stored in <c>job.json</c> (complexity and security), so the runner never reads it again and
/// the route it runs is the route it was shown (spec A, "Router").
/// </summary>
/// <remarks>
/// The file is read the way the work-plan validator reads one: the strict YAML reader, at most 64 KiB, scalars of <c>kind</c>, <c>complexity</c> and <c>security</c> only. A file that is not there, is too large, does not
/// read, or names none of them gives <see cref="RouteRequest.NoComplexity"/> and no kind — the router then takes the role's own list, and never refuses a job for a task file it could not read (the role reads the file
/// itself and says what is wrong with it).
/// </remarks>
internal static class RoleTaskFacts
{
    public static (string Kind, int Complexity, bool Security) Read(string repoPath, string taskFile)
    {
        var none = (string.Empty, RouteRequest.NoComplexity, false);
        if (taskFile.Length == 0 || TextOf(repoPath, taskFile) is not { } text)
        {
            return none;
        }

        return new StrictYamlReader().Read(text) is YamlRead.Parsed { Root: YamlMap map }
            ? (Scalar(map, "kind")?.Text ?? string.Empty, Complexity(map), Scalar(map, "security")?.Boolean == true)
            : none;
    }

    private static int Complexity(YamlMap map) =>
        Scalar(map, "complexity") is { Kind: YamlScalarKind.Integer } scalar && scalar.Integer is >= 0 and <= ImportantRule.TopComplexity ? (int)scalar.Integer : RouteRequest.NoComplexity;

    private static YamlScalar? Scalar(YamlMap map, string key) => map.Find(key)?.Value as YamlScalar;

    /// <summary>
    /// The file's text when it is inside the repository, readable and not over the reader's cap; else null. "Inside" is decided on REAL paths (<see cref="RealPath"/>, the one road that resolves links): a task file that is a link to a place outside
    /// the repository is not read.
    /// </summary>
    private static string? TextOf(string repoPath, string taskFile)
    {
        try
        {
            var root = RealPath.Resolve(repoPath);
            var file = RealPath.Resolve(taskFile, repoPath);
            if (!root.Ok || !file.Ok || !IsBelow(root.Path, file.Path))
            {
                return null;
            }

            var info = new FileInfo(file.Path);

            return info.Exists && info.Length <= StrictYamlReader.MaxBytes ? File.ReadAllText(file.Path, Encoding.UTF8) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="file"/> is a path below <paramref name="root"/>: its way from the root names no <c>..</c> and is not another root.</summary>
    private static bool IsBelow(string root, string file)
    {
        var way = Path.GetRelativePath(root, file);

        return !Path.IsPathRooted(way) && !way.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("..") && way != ".";
    }
}

/// <summary>The router at the start of a job: the settings' roles table, the task's facts, the descriptor's role — one answer, a route or a refusal sentence.</summary>
internal static class RoleRouting
{
    /// <summary>
    /// The route of a job for <paramref name="descriptor"/>, from the layered <paramref name="settings"/>. A role with no <c>RouteAs</c> asks no model and gets an empty route.
    /// </summary>
    public static (RoleJobPlan? Plan, string Refusal) Plan(RoleDescriptor descriptor, RoleJobRequest request, Func<string, string?> settings)
    {
        if (descriptor.RouteAs.Length == 0)
        {
            return (new RoleJobPlan([], RouteRequest.NoComplexity, false), string.Empty);
        }

        var (kind, complexity, security) = RoleTaskFacts.Read(request.RepoPath, request.TaskFile);
        var roles = RolesAnswer.Compute(settings, string.Empty, string.Empty, claudeHook: false);

        var decided = RoleRouter.Route(new RouteRequest(descriptor.RouteAs, request.Model, kind, complexity, security), new RouteTable(roles.Roles, roles.ComplexityBands));

        // The coder's launch is claude only: its route keeps the claude entries, and a route with none is refused here, before any folder. The judge's named model must be on the important list.
        return Narrowed(descriptor, decided, request, roles.Roles) switch
        {
            RouteResult.Routed routed => (new RoleJobPlan(RoleRoute.Format(routed.Models), complexity, security), string.Empty),
            RouteResult.Refused refused => (null, refused.Reason),
            _ => (null, "the router gave no answer"),
        };
    }

    /// <summary>The router's answer after the rule of the role: the coder keeps claude entries only (<see cref="CoderRoute"/>), the judge refuses a named model outside the important list (<see cref="JudgeRoute"/>).</summary>
    private static RouteResult Narrowed(RoleDescriptor descriptor, RouteResult decided, RoleJobRequest request, IReadOnlyList<(string Role, IReadOnlyList<ModelRef> Models)> lists) => descriptor.RouteAs switch
    {
        RoleRouter.CoderRole => CoderRoute.Narrow(decided, request.Model),
        RoleRouter.ImportantRole => JudgeRoute.Narrow(decided, request.Model, lists.FirstOrDefault(list => list.Role == RoleRouter.ImportantRole).Models ?? []),
        _ => decided,
    };
}

/// <summary>What the router left for the job: its route (<c>vendor/model</c>, in order), and the task facts the route was made from.</summary>
internal sealed record RoleJobPlan(IReadOnlyList<string> Route, int Complexity, bool Security);
