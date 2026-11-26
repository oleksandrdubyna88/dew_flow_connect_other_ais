using CoaiMcp.Core.Notices;
using CoaiMcp.Server.Keys;

namespace CoaiMcp.Tests;

/// <summary>
/// What the launch-wiring tests of the claude key pool (epoch 3 E2 s7 t5) need around a rig: keys put into the rig's own data folder through the product's own admin, and what FakeCli recorded of each launch — which key
/// ID it was started with and which variable names it had (never the text: the fake records the id the product would make).
/// </summary>
internal static class KeyRig
{
    /// <summary>Adds one made-up key per tag to the pool of <paramref name="dataDir"/>, in order, labelled by the tag. Answers their ids.</summary>
    internal static IReadOnlyList<string> AddKeys(string dataDir, params string[] tags) =>
        [.. tags.Select(tag => AddKey(dataDir, tag))];

    internal static string AddKey(string dataDir, string tag, string label = "")
    {
        var admin = new KeyAdmin(new KeyStoreAccess(dataDir, TimeProvider.System, _ => { }));
        var added = admin.Add(new KeyInput(1, label.Length > 0 ? label : tag, KeyWorld.Token(tag), string.Empty, KeySources.Local));
        Thread.Sleep(2);

        return added.Value?.Id ?? throw new InvalidOperationException($"the test key '{tag}' was refused: {added.Refusal}");
    }

    /// <summary>The pool of <paramref name="dataDir"/> as the product's own list shows it.</summary>
    internal static IReadOnlyList<KeyRow> Rows(string dataDir) =>
        new KeyAdmin(new KeyStoreAccess(dataDir, TimeProvider.System, _ => { })).List(false).Value!.Rows;

    /// <summary>The ledger of <paramref name="rows"/> by id.</summary>
    internal static KeyRow Row(string dataDir, string id) => Rows(dataDir).Single(row => row.Id == id);

    /// <summary>The sidecar files of the launches recorded in <paramref name="folder"/> with the extension <paramref name="extension"/>, in the order the launches were made.</summary>
    private static IEnumerable<FileInfo> Sidecars(string folder, string extension) =>
        Directory.Exists(Path.Combine(folder, "sidecars"))
            ? new DirectoryInfo(Path.Combine(folder, "sidecars")).EnumerateFiles("*" + extension).OrderBy(file => file.CreationTimeUtc).ThenBy(file => file.LastWriteTimeUtc)
            : [];

    /// <summary>The id of the key each recorded launch in <paramref name="folder"/> was started with, in the order they were made (empty for a launch that had none).</summary>
    internal static IReadOnlyList<string> KeyIdsOf(string folder) =>
        [.. Sidecars(folder, ".keyid").Select(file => File.ReadAllText(file.FullName).Trim())];

    /// <summary>The variable NAMES each recorded launch had, in the order they were made.</summary>
    internal static IReadOnlyList<IReadOnlyList<string>> EnvNamesOf(string folder) =>
        [.. Sidecars(folder, ".envnames").Select(file => (IReadOnlyList<string>)File.ReadAllLines(file.FullName))];

    /// <summary>The home values (<c>HOME</c>, <c>CLAUDE_CONFIG_DIR</c>, the <c>XDG_*</c> names) each recorded launch had, as name to value.</summary>
    internal static IReadOnlyList<IReadOnlyDictionary<string, string>> HomesOf(string folder) =>
        [.. Sidecars(folder, ".envhome").Select(file => (IReadOnlyDictionary<string, string>)File.ReadAllLines(file.FullName).Select(line => line.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]))];

    /// <summary>The one line a notice is rendered as in the log and on the page.</summary>
    internal static string Line(ServerNotice notice) => ServerNoticeLine.Of(notice);
}
