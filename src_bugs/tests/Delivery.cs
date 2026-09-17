using System.Text;

namespace CoaiBugs.Tests;

/// <summary>
/// How the deployment delivers a value, so a test feeds the server what the host will.
/// </summary>
/// <remarks>
/// <para>The administrator list is newline-separated and a systemd <c>EnvironmentFile</c> assignment
/// cannot hold a newline, so it travels base64 on one line — the secret store holds that, the forced
/// command passes it through, and the server decodes it. A fixture that set the raw text would be
/// testing a shape no host ever produces, and every admin test would pass while the deployed server
/// refused to start.</para>
/// <para>ONE place, used by both harnesses: the in-process <see cref="BugsServer"/> and the
/// real-binary scenario. Two copies of an encoding rule is two chances to encode it differently from
/// the deployment.</para>
/// </remarks>
internal static class Delivery
{
    /// <summary>Exactly what <c>base64 -w0</c> makes of the list, or nothing when there is none.</summary>
    internal static string? AdminKeys(string? list) =>
        list is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(list));
}
