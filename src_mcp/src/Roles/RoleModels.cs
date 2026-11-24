namespace CoaiMcp.Roles;

/// <summary>One model of a role's list — the model's name and the vendor whose CLI runs it. A role's list is ORDERED: the first is the model, the later ones the failover order.</summary>
internal sealed record ModelRef(string Model, string Vendor);

/// <summary>A complexity band: tasks scored <see cref="Min"/> to <see cref="Max"/> run <see cref="Model"/>.</summary>
internal sealed record Band(int Min, int Max, string Model, string Vendor);

/// <summary>The work method's numbers (server.md §8) — read by the validator and the instructions, never typed into them.</summary>
internal sealed record WorkMethod(int MvpMin, int MvpMax, int MaxEpicsPerEpoch, int MinConsultsPerEpoch, int MinConsultsPerEpic);

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
