using System.Collections;

namespace CoaiMcp.Server.Keys;

/// <summary>
/// The two generic sources, synced into the pool at every start (and on <c>--keys sync</c>): the environment variables of the coai process, and the file at <c>COAI_CLAUDE_KEYS_FILE</c> or the device setting
/// <c>keysFile</c> (KEY_POOL_SPEC.md, the owner's correction of 2026-11-23).
/// </summary>
/// <remarks>
/// <para>Nothing is created when nothing is configured: no variable, no file path and no earlier source key means no folder. The values of <c>COAI_CLAUDE_KEY_*</c> reach no child process except the ONE chosen key as
/// <c>CLAUDE_CODE_OAUTH_TOKEN</c> (the minimal environment of a launch already drops every <c>COAI_</c> name; a test proves it for these names).</para>
/// <para>A configured path whose file is missing, has a wrong mode or cannot be parsed is a refusal that touches nothing: the keys it listed before stay as they were.</para>
/// </remarks>
internal static class KeySourceSync
{
    internal const string FileSetting = "COAI_CLAUDE_KEYS_FILE";

    internal const string FileSettingName = "keysFile";

    public static KeySyncAnswer Run(KeyStoreAccess access, Func<string, string?> settings, IEnumerable<KeyValuePair<string, string>> variables)
    {
        var admin = new KeyAdmin(access);
        var sync = new KeySync(access, admin);
        var all = variables.ToList();
        var results = new List<KeySourceResult>();

        // The environment source is swept ONLY when at least one of its variables is there (the judgement of s7 part 1): a process started without them is not a rotation that removed every key, and a start must not disable the keys a person added through the environment.
        if (all.Any(pair => pair.Key.StartsWith(KeySourceReader.KeyPrefix, StringComparison.Ordinal)))
        {
            results.Add(Apply(sync, KeySources.Environment, KeyResult<SourceRead>.Of(KeySourceReader.FromEnvironment(all))));
        }

        if (KeysFilePath(settings) is { Length: > 0 } path)
        {
            var read = KeySourceReader.FromFile(path, access.Folder.ModeOfFile);
            results.Add(Apply(sync, KeySources.File(Path.GetFullPath(path)), read));
        }

        return new KeySyncAnswer(results);
    }

    /// <summary>The path the person chose: the environment variable, else the device setting.</summary>
    public static string KeysFilePath(Func<string, string?> settings) =>
        (settings(FileSetting) is { Length: > 0 } named ? named : settings(FileSettingName) ?? string.Empty).Trim();

    private static KeySourceResult Apply(KeySync sync, string source, KeyResult<SourceRead> read)
    {
        if (read.Value is not { } listed)
        {
            return new KeySourceResult(source, null, read.Refusal);
        }

        var applied = sync.Apply(source, listed);

        return new KeySourceResult(source, applied.Value, applied.Refusal);
    }

    /// <summary>The process environment as pairs (the real one; a test passes its own).</summary>
    public static IEnumerable<KeyValuePair<string, string>> ProcessVariables() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().Select(entry => new KeyValuePair<string, string>((string)entry.Key, entry.Value as string ?? string.Empty));

    /// <summary>Whether a start has anything to sync: a variable of the family or a keys file path. A start with neither does not touch the disk.</summary>
    public static bool Configured(Func<string, string?> settings, IEnumerable<KeyValuePair<string, string>> variables) =>
        KeysFilePath(settings).Length > 0 || variables.Any(pair => pair.Key.StartsWith(KeySourceReader.KeyPrefix, StringComparison.Ordinal));
}
