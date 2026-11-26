using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The properties of a coder's unit, written into the job's record (epoch 3 E2 s6, the recorded change): <c>attempts/&lt;n&gt;/unit-&lt;part&gt;.json</c>, one file for each launch. The unit lives for seconds and is gone when its job ends, so an observer that looks for its
/// properties afterwards finds nothing — the pool-key proof (<c>RESULTS_coder_proof.md</c>, t5) could not read them and said so. The file is the list the unit WAS STARTED with (<see cref="BoundaryRecord.Properties"/>), nothing added or left out, with the network state and its reason, the preflight rows
/// and the submodule mounts of the job.
/// </summary>
/// <remarks>
/// <b>It holds names and non-secret values, never an environment.</b> A property whose name says it carries an environment or a credential (<c>Environment</c>, <c>EnvironmentFile</c>, <c>PassEnvironment</c>, <c>SetCredential</c>, <c>LoadCredential</c>…) is written with its NAME and the value
/// <c>(withheld)</c>, whatever the list it is made from holds. coai puts none of them into a unit today (a secret travels on the unit's stdin); this is the rule that keeps a later one from landing in a file that is kept.
/// </remarks>
public static class CoderUnitRecord
{
    /// <summary>What a withheld property's value is written as.</summary>
    internal const string Withheld = "(withheld)";

    /// <summary>The file name of a launch's record.</summary>
    /// <summary>The unit's name for one launch of <paramref name="job"/> (<c>coai-coder-&lt;job&gt;-&lt;part&gt;</c>).</summary>
    public static string UnitName(BoundaryJob job, string part) => CoderUnit.UnitName(job, part);

    internal static string FileName(string part) => $"unit-{part}.json";

    /// <summary>
    /// Whether the value of a property named <paramref name="name"/> is never written: the name says environment or credential.
    /// </summary>
    internal static bool IsWithheld(string name) =>
        name.Contains("Environment", StringComparison.OrdinalIgnoreCase) || name.Contains("Credential", StringComparison.OrdinalIgnoreCase) || name.Contains("Secret", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the record of one launch into <paramref name="folder"/> (made when it is not there), by a temp file moved over the name, so a reader never sees half a file. Returns whether it was written; an I/O refusal is not a failure of the launch — the record is evidence.
    /// </summary>
    /// <param name="folder">The attempt's folder of the job's record.</param>
    /// <param name="part">The unit's part name (<c>c1</c>…), the file's last word.</param>
    /// <param name="unit">The unit's name.</param>
    /// <param name="record">What the launch was bounded by and what the preflight said.</param>
    /// <param name="mounts">The submodules of the job and whether each was provided.</param>
    /// <param name="started">False when the preflight refused the launch and no unit of the coder ran.</param>
    public static bool Write(string folder, string part, string unit, BoundaryRecord record, CoderMounts mounts, bool started = true)
    {
        if (!CoderUnit.IsPart(part))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, FileName(part));
            var temp = target + ".tmp";
            File.WriteAllBytes(temp, Render(part, unit, record, mounts, started));
            File.Move(temp, target, overwrite: true);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static byte[] Render(string part, string unit, BoundaryRecord record, CoderMounts mounts, bool started)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("unit", unit);
            json.WriteString("part", part);
            json.WriteBoolean("started", started);
            json.WriteString("boundary", record.Boundary);
            json.WriteString("network", record.Network);
            json.WriteString("networkReason", record.NetworkReason);
            json.WriteStartArray("properties");
            foreach (var property in record.Properties)
            {
                var cut = property.IndexOf('=', StringComparison.Ordinal);
                var name = cut < 0 ? property : property[..cut];
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteString("value", cut < 0 ? string.Empty : IsWithheld(name) ? Withheld : property[(cut + 1)..]);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("preflight");
            foreach (var row in record.Preflight)
            {
                json.WriteStartObject();
                json.WriteString("item", row.Item);
                json.WriteBoolean("held", row.Held);
                json.WriteString("detail", row.Detail);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("mounts");
            foreach (var mount in mounts.Declared)
            {
                json.WriteStartObject();
                json.WriteString("name", mount.Name);
                json.WriteString("path", mount.Path);
                json.WriteString("pin", mount.Pin);
                json.WriteBoolean("provided", mount.Provided);
                json.WriteString("note", mount.Note);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return [.. stream.ToArray(), .. Encoding.UTF8.GetBytes("\n")];
    }
}
