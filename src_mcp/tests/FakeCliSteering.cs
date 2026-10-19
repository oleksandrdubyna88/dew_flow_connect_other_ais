using System.Text.Json;
using CoaiMcp.FakeCli;

namespace CoaiMcp.Tests;

/// <summary>
/// The fake CLI's steering, from the test side: the ONE writer of its minimal-environment file, and the one place a
/// test clears the <c>FAKECLI_*</c> variables it holds.
/// </summary>
/// <remarks>
/// <para><b>The file is beside the fake's binary</b> (<see cref="MinimalSteeringFile"/>), not in the user's temp folder: a child
/// started on a minimal or a built environment carries no <c>FAKECLI_*</c> variable, so a file is the only way to steer it, and the
/// temp folder is shared by every run of one Windows user — two worktrees' runs steered each other's fakes
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, C1/D1).</para>
/// <para><b>Inside ONE run the file is safe only while every class that writes it is in the serial <c>fakecli-env</c> collection</b>,
/// which runs alone. This helper being the only writer keeps that checkable: <c>EveryWriterOfTheSteeringFileTests</c> fails when
/// a class that calls it is outside the collection, or when any other test source spells the file's name.</para>
/// </remarks>
internal static class FakeCliSteering
{
    /// <summary>What a class holds before it has written anything: disposing it deletes nothing.</summary>
    public static IDisposable Unwritten { get; } = new Lease(deletes: false);

    /// <summary>The file's path: beside the fake's binary.</summary>
    public static string MinimalFile => MinimalSteeringFile.Location;

    /// <summary>Writes the steering file (replacing what is there). Disposing the result deletes it.</summary>
    public static IDisposable Write(IReadOnlyDictionary<string, string> pairs)
    {
        File.WriteAllText(MinimalFile, JsonSerializer.Serialize(pairs));

        return new Lease(deletes: true);
    }

    /// <summary>What the steering file holds now — for a test that adds one name to it.</summary>
    public static Dictionary<string, string> ReadBack() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(MinimalFile)) ?? [];

    /// <summary>
    /// Deletes a steering file a killed run left beside the binary, so the first vendor-shaped launch of this run is not steered
    /// by a run that never cleaned up. Answers whether there was one.
    /// </summary>
    public static bool DeleteALeftover()
    {
        var left = File.Exists(MinimalFile);
        File.Delete(MinimalFile);

        return left;
    }

    /// <summary>Clears every <c>FAKECLI_*</c> variable this process holds.</summary>
    /// <remarks>
    /// The vault's probe made several more of them matter to the vault tests, so a class that sets only its own and inherits the
    /// rest from whichever test ran before can see a "--help exited 3" it never asked for (own review).
    /// </remarks>
    public static void Reset()
    {
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>()
                     .Where(name => name.StartsWith("FAKECLI_", StringComparison.Ordinal))
                     .ToList())
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <summary>Deletes the steering file when disposed — the writer's own cleanup; a killed run's is <see cref="DeleteALeftover"/>'s.</summary>
    private sealed class Lease(bool deletes) : IDisposable
    {
        public void Dispose()
        {
            if (deletes)
            {
                File.Delete(MinimalFile);
            }
        }
    }
}
