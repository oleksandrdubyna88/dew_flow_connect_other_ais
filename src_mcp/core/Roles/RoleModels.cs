namespace CoaiMcp.Core.Roles;

/// <summary>One model of a role's list — the model's name and the vendor whose CLI runs it. A role's list is ORDERED: the first is the model, the later ones the failover order.</summary>
internal sealed record ModelRef(string Model, string Vendor);

/// <summary>A complexity band: tasks scored <see cref="Min"/> to <see cref="Max"/> run <see cref="Model"/>.</summary>
internal sealed record Band(int Min, int Max, string Model, string Vendor);
