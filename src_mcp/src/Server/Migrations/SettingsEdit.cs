using System.Text.Json.Nodes;

namespace CoaiMcp.Server.Migrations;

/// <summary>
/// The settings after the v2 move, and what moved — pure over two JSON objects, so the rules are a unit test and the dry run and the apply cannot
/// disagree about them.
/// </summary>
/// <remarks>
/// A removed key leaves <c>settings.json</c> for the removed file with its value kind kept (a number stays a number); an existing key of the removed file is
/// never overwritten. A renamed key is rewritten under its new name where it stood; when both names exist the NEW one wins and the old value goes to the
/// removed file. Keys are compared without case, as the settings reader's own dictionary does. The inputs are never changed.
/// </remarks>
/// <param name="Settings">The settings object after the move.</param>
/// <param name="Removed">The removed-keys file after the move.</param>
/// <param name="RemovedKeys">The keys that left <c>settings.json</c> as removed, as they were spelled.</param>
/// <param name="RenamedKeys">The old names of the keys that were renamed (or dropped because the new name was there), as they were spelled.</param>
internal sealed record SettingsEdit(JsonObject Settings, JsonObject Removed, IReadOnlyList<string> RemovedKeys, IReadOnlyList<string> RenamedKeys)
{
    /// <summary>Whether the move changes anything at all.</summary>
    public bool Any => RemovedKeys.Count + RenamedKeys.Count > 0;

    public static SettingsEdit Of(JsonObject settings, JsonObject removedFile, MigrationV2Tables tables)
    {
        var kept = (JsonObject)settings.DeepClone();
        var removed = (JsonObject)removedFile.DeepClone();
        List<string> removedKeys = [];
        List<string> renamedKeys = [];

        foreach (var key in tables.RemovedSettings)
        {
            if (Find(kept, key) is { } present)
            {
                MoveOut(kept, removed, present);
                removedKeys.Add(present);
            }
        }

        foreach (var (old, renamed) in tables.RenamedSettings)
        {
            if (Find(kept, old) is not { } oldKey)
            {
                continue;
            }

            if (Find(kept, renamed) is not null)
            {
                MoveOut(kept, removed, oldKey);
            }
            else
            {
                kept = Renamed(kept, oldKey, renamed);
            }

            renamedKeys.Add(oldKey);
        }

        return new SettingsEdit(kept, removed, removedKeys, renamedKeys);
    }

    private static string? Find(JsonObject json, string key) =>
        json.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

    private static void MoveOut(JsonObject from, JsonObject removed, string key)
    {
        if (Find(removed, key) is null)
        {
            removed[key] = from[key]?.DeepClone();
        }

        from.Remove(key);
    }

    private static JsonObject Renamed(JsonObject json, string old, string renamed)
    {
        var result = new JsonObject();
        foreach (var (key, value) in json)
        {
            result[key == old ? renamed : key] = value?.DeepClone();
        }

        return result;
    }
}
