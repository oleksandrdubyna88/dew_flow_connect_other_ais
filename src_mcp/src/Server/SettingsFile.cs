using System.Text.Json;

namespace CoaiMcp.Server;

/// <summary>
/// The settings the extension writes into the data directory, read at startup.
/// </summary>
/// <remarks>
/// <para><b>Why a file at all.</b> Settings used to travel only inside the `mcpServers` env block,
/// which meant every change to a threshold or a language demanded that a person copy the block
/// again and re-paste it into their client. That is a chore invented by an implementation detail:
/// the extension and the server already share a directory — sessions and escalations live there —
/// so the settings can too, and the pasted block goes back to being what it should be, a path to
/// a binary, pasted once.</para>
/// <para><b>Environment still wins.</b> A variable in the client's config is more specific than a
/// file that any window may rewrite, and it is what a scripted or containerised run has. So the
/// file is the base and the environment overrides it, key by key — never the other way round.</para>
/// <para>A missing file is the normal case (nobody has opened the panel yet). A malformed one is
/// ignored with the defaults left standing: a review run against half-written settings is worse
/// than one run against the shipped ones.</para>
/// </remarks>
public static class SettingsFile
{
    public const string Name = "settings.json";

    /// <summary>
    /// Reads the file as an env-shaped lookup, so the caller's precedence stays one line.
    /// </summary>
    /// <remarks>
    /// <para><b>It adopts first, and that is why the adoption lives here rather than in startup.</b>
    /// <c>--providers</c> can be the first thing that ever runs against an installation partitioned
    /// before the settings file knew about sides: it would read an absent side file, report defaults
    /// for a machine whose configuration exists, and a normal start afterwards would adopt and answer
    /// differently about that same machine. Whatever reads the settings adopts first, so there is one
    /// road. (The plan round found the seam; the consultation named the check.)</para>
    /// <para><b><paramref name="said"/> has no default, and that is the enforcement.</b> The first
    /// build called <see cref="AdoptRootSettings"/> here and dropped what it returned on the floor,
    /// while this comment claimed the startup path logged it — nothing anywhere called it. Six
    /// reviewers across three vendors found the same thing, and they were describing the defect this
    /// whole family of work exists for: a sentence that is composed and shown to nobody. A migration
    /// that moves a person's configuration, or REFUSES to, is exactly what they need told. Making the
    /// sink required means the compiler asks every future caller where its sentences go, which a
    /// ratchet test could only ask after the fact.</para>
    /// <para>A caller whose stdout carries a protocol — <c>--providers</c> prints JSON, the stdio
    /// host speaks JSON-RPC — passes a sink that writes to stderr or to the log, never to stdout
    /// (<c>logging-serilog.md</c>).</para>
    /// </remarks>
    public static Func<string, string?> Layer(string dataDir, Func<string, string?> environment, Action<string> said)
    {
        foreach (var sentence in AdoptRootSettings(environment))
        {
            said(sentence);
        }

        var fromFile = Read(Path.Combine(dataDir, Name));
        return name => environment(name) is { Length: > 0 } fromEnv ? fromEnv : fromFile.GetValueOrDefault(name);
    }

    internal static Dictionary<string, string> Read(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                // Everything reaches the server as a string, exactly as an environment variable
                // would — one parser for both sources, so neither can drift from the other.
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.ToString(),
                    JsonValueKind.Array or JsonValueKind.Object => property.Value.GetRawText(),
                    _ => null,
                };
                if (value is { Length: > 0 })
                {
                    values[property.Name] = value;
                }
            }

            return values;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Where the data directory is — the same rule the rest of the product uses, ASKED rather than
    /// repeated.
    /// </summary>
    /// <remarks>
    /// <para>It used to be its own rule: <c>environment("COAI_DATA_DIR") is { Length: &gt; 0 } dir ?
    /// dir : DefaultDataDir</c>, with no side and no trim. So <c>coai.db</c> and <c>sessions/</c>
    /// went to <c>&lt;root&gt;/&lt;side&gt;/</c> while <c>settings.json</c> and <c>logs/</c> stayed
    /// in <c>&lt;root&gt;/</c>, and two installations that share one NAS — the whole reason
    /// <c>COAI_DATA_SIDE</c> exists — overwrote each other's configuration in silence. A whitespace
    /// <c>COAI_DATA_DIR</c> was a configured directory to this and an unset one to the other, so the
    /// two halves disagreed about that too.</para>
    /// <para><b>Seven callers read this</b>: two one-shot modes, the LOG ROOT (through
    /// <c>CoaiLogPath.RootFor</c>), the <c>--providers</c> layer, the settings layer the server runs
    /// on, and the file its change watcher stats. One correction reaches all of them, and the log
    /// root partitions as a consequence rather than by a rule of its own.</para>
    /// </remarks>
    public static string DataDirFrom(Func<string, string?> environment) =>
        PanelSettings.DataDirectoryFor(environment);

    /// <summary>
    /// A side that has no settings file of its own takes the one both sides used to share.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it exists.</b> Before the side reached <c>settings.json</c>, every partitioned
    /// installation read <c>&lt;root&gt;/settings.json</c>. Moving that file under the side without
    /// adopting what is in it would start those installations on DEFAULTS — a person's vendors, keys
    /// and round budgets gone at the moment they upgrade, and nothing saying so.</para>
    /// <para><b>Why it is HERE and not in startup.</b> <c>--providers</c> runs before any normal
    /// start, reads the settings layer, and would otherwise report defaults for an installation
    /// whose configuration exists. Every reader reaches this, so the adoption sits with the read.
    /// (The plan round, and the consultation named the check for it.)</para>
    /// <para><b>The protocol, and each step answers a way of getting it wrong that a reviewer
    /// named.</b> The root file is NEVER removed, or the second side to start finds nothing and
    /// reverts to defaults. The copy is written to a temporary sibling and published by rename, or
    /// a kill mid-write leaves a partial file that blocks adoption for ever. The rename does NOT
    /// overwrite, so two sides racing end with the winner's file and a loser that says so rather
    /// than last-writer-wins. And a root file that does not PARSE is named and left alone, because
    /// publishing an unreadable file to a second location is copying a defect into a second
    /// place.</para>
    /// <para>A genuinely new side adopts it too, and that is deliberate: it is what that side would
    /// have read yesterday, so adopting preserves behaviour where starting on defaults would be the
    /// surprise.</para>
    /// <para><b>Why it takes no lock, asked by four reviewers across two vendors.</b> The lock is the
    /// EXTENSION's — <c>settings.lock</c>, taken in <c>extension.ts</c> around a WRITE of this same
    /// file — and the server has never had a client for it. It does not need one here: the rename
    /// below is <c>overwrite: false</c>, so this operation is create-if-absent and ATOMIC. An
    /// advisory lock file would be weaker, not stronger — it can be stale, broken or ignored, while a
    /// rename that refuses cannot lose a write. A window writing the same path at the same moment
    /// wins and this side reads what it wrote; a partial root file being written by an old extension
    /// fails to parse, is named, and nothing is copied.</para>
    /// <para><b>Why a READ does a write at all</b>, which is the other thing reviewers keep asking.
    /// Because the alternative was measured and is worse: <c>--providers</c> can be the first thing
    /// that runs, and adoption in the startup path would have it report defaults for a machine whose
    /// configuration exists. On a filesystem that will not take the write — read-only, a mount gone,
    /// no permission — nothing here throws: every failure is caught and returned as a sentence, so a
    /// read stays a read and the caller still gets its settings.</para>
    /// </remarks>
    /// <returns>What a person should be told, or empty when nothing happened.</returns>
    public static IReadOnlyList<string> AdoptRootSettings(Func<string, string?> environment)
    {
        var dataDir = PanelSettings.DataDirectoryFor(environment);
        var root = PanelSettings.DataRootFor(environment);
        if (string.Equals(dataDir, root, StringComparison.Ordinal))
        {
            // No side asked for: there is one settings file and this must not invent a second.
            return [];
        }

        var destination = PathFor(dataDir);
        var legacy = PathFor(root);
        if (File.Exists(destination) || !File.Exists(legacy))
        {
            return [];
        }

        string text;
        try
        {
            text = File.ReadAllText(legacy);
            using var parsed = JsonDocument.Parse(text);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [Unreadable(legacy, "it is not a JSON object")];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [Unreadable(legacy, e.Message)];
        }

        return Publish(text, destination, legacy);
    }

    /// <summary>
    /// A test's only way into the instant between "the destination is not there" and the rename.
    /// </summary>
    /// <remarks>
    /// <para>It exists because the race test did not test the race. It created the destination
    /// BEFORE calling the adoption, so the existence check returned early and
    /// <c>File.Move(overwrite: false)</c> — the whole guarantee — was never reached: the test would
    /// have passed with the guard deleted, which is the same way a test stops meaning anything as
    /// the one caught on the role-deletion nonce. A race is only a race from INSIDE, so the other
    /// side of it has to be able to finish here.</para>
    /// <para>It carries the destination so a test acts only on its own directory: xUnit runs classes
    /// in parallel and a hook that fires blind would reach into another one's adoption.</para>
    /// </remarks>
    internal static Action<string>? BetweenTheCheckAndThePublication;

    private static IReadOnlyList<string> Publish(string text, string destination, string legacy)
    {
        // Random rather than the pid alone. The pid is predictable and this directory can be a share,
        // so a name anyone could precompute is a name anyone could pre-create — and the pid is not
        // even unique across two machines mounting one NAS, which is the case this feature is for.
        var beside = $"{destination}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(beside, text);
            BetweenTheCheckAndThePublication?.Invoke(destination);

            // `overwrite: false` is what makes a concurrent winner safe: the loser throws here, says
            // so, and the file that is already published stands.
            File.Move(beside, destination, overwrite: false);

            return [
                $"This side had no settings of its own, so it adopted {legacy} — the file both sides "
                + $"read before they were partitioned. It is now {destination}, and the original is "
                + "left where it is for any other side that has not started yet."];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another side published first, or the directory will not take the file. Either way this
            // side reads what is there rather than replacing it.
            return File.Exists(destination)
                ? []
                : [$"{legacy} could not be adopted into {destination}: {e.Message}. This side is "
                   + "starting on defaults, and the original file is untouched."];
        }
        finally
        {
            try
            {
                File.Delete(beside);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A temporary file that outlives its run is swept by the next adoption, which writes
                // its own name. Failing the start over it would be the worse trade.
            }
        }
    }

    private static string Unreadable(string legacy, string why) =>
        $"{legacy} exists and could not be read ({why}), so this side did not adopt it and is "
        + "starting on defaults. Nothing was changed. Repair that file, or open the panel and save "
        + $"the settings once to write a fresh {Name} for this side — either ends this message.";

    /// <summary>The settings file itself — what a change watcher has to stat.</summary>
    public static string PathFor(string dataDir) => Path.Combine(dataDir, Name);
}
