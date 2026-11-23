using System.Collections;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The environment a role runner is given — through <c>env.json</c>, never through argv or a unit's properties (spec A). A <c>systemd-run --user</c> unit starts from the user MANAGER's
/// environment, not the launcher's: measured on 2026-11-23, a variable set in the launcher was unset in the unit, and <c>PATH</c> lacked <c>~/.local/bin</c> and the node folder, so a <c>claude</c>
/// found by the launcher was not found by its runner (<c>v2/spikes/RESULTS_detach.md</c> §3). So the launcher writes the names the CLIs need into the job folder, and the runner restores them.
/// </summary>
/// <remarks>
/// <para><b>Names, not a copy of everything.</b> The list is <see cref="ProcessEnvironment.Passthrough"/> (what every confined launch here already copies: <c>PATH</c>, <c>HOME</c>, the locale, the
/// proxy and certificate names, the temporary folder), <c>DOTNET_ROOT</c> (a framework-dependent runner outside the default place cannot start without it), and this product's own folder, role
/// and log names (<c>COAI_DATA_</c>, <c>COAI_ROLE_</c>, <c>COAI_ROLES_</c>, <c>COAI_LOG_</c>) and the coder's worktree root (<c>COAI_CODER_ROOT</c>, so the folder the person chose is the one the runner uses) and the one setting of a coder job's loop that has no <c>COAI_ROLE_</c> name, <c>COAI_FIX_ROUNDS</c> (<see cref="FixRounds"/>). A vendor key, a token or another program's variable is not on it, so it is never written to disk by
/// a role start; the runner applies the list again on the way in, so a hand-edited file cannot widen it.</para>
/// </remarks>
internal static class RoleRunEnvironment
{
    private static readonly string[] OwnPrefixes = ["COAI_DATA_", "COAI_ROLE_", "COAI_ROLES_", "COAI_LOG_", "COAI_CODER_"];

    /// <summary>True for a name the runner may be handed.</summary>
    public static bool Allowed(string name) =>
        name.Length > 0
        && (ProcessEnvironment.Passthrough.Contains(name)
            || string.Equals(name, "DOTNET_ROOT", StringComparison.Ordinal)
            || string.Equals(name, FixRounds.Key, StringComparison.Ordinal)
            || OwnPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));

    /// <summary>The allowed variables of <paramref name="source"/> (a process environment, <see cref="Environment.GetEnvironmentVariables()"/>), by name.</summary>
    public static RoleJobEnvironment Capture(IDictionary source)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in source)
        {
            if (entry.Key is string name && entry.Value is string value && Allowed(name))
            {
                variables[name] = value;
            }
        }

        return new RoleJobEnvironment(variables);
    }

    /// <summary>Sets the allowed variables of <paramref name="environment"/> in THIS process, so the work's children inherit them; answers how many were set.</summary>
    public static int Apply(RoleJobEnvironment environment)
    {
        var set = 0;
        foreach (var (name, value) in environment.Variables ?? new Dictionary<string, string>())
        {
            if (Allowed(name))
            {
                Environment.SetEnvironmentVariable(name, value);
                set++;
            }
        }

        return set;
    }
}
