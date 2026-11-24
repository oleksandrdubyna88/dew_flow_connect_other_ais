using CoaiMcp.Core.Roles;
using CoaiMcp.Core.Work;

namespace CoaiMcp.Roles;

/// <summary>The work method's numbers (server.md §8) — read by the validator (<c>validate_work</c>) and the instructions, never typed into them.</summary>
internal sealed record WorkMethod(int MvpMin, int MvpMax, int MaxEpicsPerEpoch, int MinConsultsPerEpoch, int MinConsultsPerEpic, int MaxStoriesPerEpic, int MaxTasksPerStory)
{
    /// <summary>The numbers the work-plan validator checks against: the device's, exactly as the <c>roles</c> answer holds them.</summary>
    internal WorkLimits ToLimits() => new(MaxEpicsPerEpoch, MvpMin, MvpMax, MaxStoriesPerEpic, MaxTasksPerStory);
}

/// <summary>The main AI the answer is for.</summary>
internal sealed record MainAi(string Vendor, string Cli, string Version);

/// <summary>A setting the answer could not use: its key and the sentence that says what was wrong. The default for that key stands.</summary>
internal sealed record RoleSettingProblem(string Key, string Sentence);

/// <summary>What <c>roles</c> answers.</summary>
internal sealed record RolesResult(
    MainAi MainAi,
    IReadOnlyList<(string Role, IReadOnlyList<ModelRef> Models)> Roles,
    IReadOnlyList<Band> ComplexityBands,
    IReadOnlyList<string> HaikuKinds,
    WorkMethod WorkMethod,
    string SettingsRevision,
    IReadOnlyList<string> Notes,
    IReadOnlyList<RoleSettingProblem> Unrecognised);
