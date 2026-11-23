namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The roles this build can start — a role needs a descriptor, and <c>start_role</c> refuses any other name BEFORE a folder is made.
/// </summary>
/// <remarks>
/// <para>Story s1 ships ONE: <c>probe</c>, a diagnostic that proves the whole detached road — launcher, lease, heartbeat, cancel, result — on a machine without asking a model anything and without
/// touching the repository (<see cref="ProbeWork"/>). The BA role is registered here by story s4; the others by the epics that build them.</para>
/// <para>A list rather than a switch, so a test hands the service its own (<see cref="RoleJobService"/>) and so the refusal can NAME the legal roles.</para>
/// </remarks>
internal static class RoleDescriptors
{
    public const string Probe = "probe";

    public static IReadOnlyList<RoleDescriptor> Shipped { get; } =
    [
        new(Probe, "a diagnostic role: runs a scripted brief (succeed, sleep N, fail WHY, child N) and asks no model anything", () => new ProbeWork()),
    ];

    public static RoleDescriptor? Find(IReadOnlyList<RoleDescriptor> descriptors, string role) =>
        descriptors.FirstOrDefault(descriptor => string.Equals(descriptor.Role, role, StringComparison.Ordinal));
}
