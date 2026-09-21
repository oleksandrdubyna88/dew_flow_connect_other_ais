namespace CoaiMcp.Server;

/// <summary>
/// Where this binary writes down what it refused, what failed, and that it died.
/// </summary>
/// <remarks>
/// <para><b>The reader shipped first, and has had nothing to read.</b>
/// <c>server-notices.jsonl</c> has been named in <c>notificationsFile.ts</c> since 2026-09-17, its
/// path derived by <c>serverNoticesPath</c>, and three modules already merge it into the panel
/// section, the page and the derived count. Nothing writes it, so every one of those surfaces
/// reports on half the product. This class is the other half; story 1.3 is its PATH, and the writer
/// arrives with story 1.4.</para>
/// <para><b>The directory is ASKED for, never composed.</b>
/// <see cref="SettingsFile.DataDirFrom"/> is the one rule — since 2026-09-18 it IS
/// <c>PanelSettings.DataDirectoryFor</c>, with the side applied and the trim applied — and the two
/// halves are held to each other by <c>shared/data-side-vectors.json</c>, which carries a
/// <c>serverNoticesPath</c> per case now beside <c>settingsPath</c> and <c>logsPath</c>. That
/// fixture exists because the failure of disagreeing here is the silent one: the server writes, the
/// extension reads an empty directory, and nothing says so.</para>
/// <para><c>Path.Combine</c> rather than string concatenation, which is what lets the same vector
/// hold on both platforms — the separator is not part of what the two halves have to agree on.</para>
/// </remarks>
public static class ServerNotices
{
    /// <summary>The file, named once — the extension spells the same name in `notificationsFile.ts`.</summary>
    public const string Name = "server-notices.jsonl";

    /// <summary>The notices file inside a data directory that has already been resolved.</summary>
    /// <remarks>
    /// It takes the directory rather than the environment on purpose: every caller here already
    /// holds one, and a second function that resolved it would be a second resolver — which is
    /// exactly the defect <c>PLAN_the_settings_file_ignores_the_side.md</c> was written to fix, four
    /// days before this file existed.
    /// </remarks>
    public static string PathFor(string dataDir) => Path.Combine(dataDir, Name);
}
