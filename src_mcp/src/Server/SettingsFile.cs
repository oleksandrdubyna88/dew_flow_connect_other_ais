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

    /// <summary>Reads the file as an env-shaped lookup, so the caller's precedence stays one line.</summary>
    public static Func<string, string?> Layer(string dataDir, Func<string, string?> environment)
    {
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
    /// Where the data directory is, decided the same way twice: this must agree with
    /// <see cref="PanelSettings.DefaultDataDir"/>, because the file lives there before any of the
    /// settings in it have been read.
    /// </summary>
    public static string DataDirFrom(Func<string, string?> environment) =>
        environment("COAI_DATA_DIR") is { Length: > 0 } dir ? dir : PanelSettings.DefaultDataDir;

    /// <summary>The settings file itself — what a change watcher has to stat.</summary>
    public static string PathFor(string dataDir) => Path.Combine(dataDir, Name);
}
