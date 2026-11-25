using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The scout's and the judge's own look at a request (epoch 3 E3 s2), made at the start BEFORE the router and before any folder is made: the brief is the question and is checked by the service (<see cref="RoleDescriptor.NeedsBrief"/>);
/// the optional <c>taskFile</c> must be a regular file inside the repository that the model may read — not a link, not under <c>.git</c>, not named like a credential file. It goes by the evidence check's own road
/// (<see cref="RoleEvidence"/>, <see cref="RoleEvidenceFiles"/>), so a path is judged the same way at the start and in the answer.
/// </summary>
internal static class ReadOnlyRoleRequest
{
    /// <summary>The sentence that refuses the request, or empty when it is fit.</summary>
    public static string Refusal(RoleJobRequest request)
    {
        if (request.TaskFile.Length == 0)
        {
            return string.Empty;
        }

        if (RoleEvidence.PathProblem("taskFile", request.TaskFile) is { Length: > 0 } problem)
        {
            return problem;
        }

        return new RoleEvidenceFiles(request.RepoPath).Probe(RoleEvidence.Normal(request.TaskFile)) is var state && state == EvidenceFileState.Ok
            ? string.Empty
            : $"taskFile '{request.TaskFile}' is not a regular file inside the repository the role may read ({Why(state)})";
    }

    private static string Why(EvidenceFileState state) => state switch
    {
        EvidenceFileState.Missing => "it is not there",
        EvidenceFileState.NotAFile => "it is not a file",
        EvidenceFileState.ThroughLink => "its path runs through a link",
        _ => "it could not be read",
    };
}
