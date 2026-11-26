using System.Globalization;

namespace CoaiMcp.Core.Work;

/// <summary>Reads the files of a pack from the folder: each one a file of the commit, within the size of a pack file, text. The first thing wrong is the refusal; nothing is cut.</summary>
internal static class WorkPackFiles
{
    /// <summary>One file of the pack, as it is shown: the repository-relative path, its size, its blob id and its text.</summary>
    internal sealed record Section(string Path, long Bytes, string Blob, string Text);

    internal sealed record Reading(IReadOnlyList<Section> Sections, string Refusal);

    /// <summary>A file the plan names: <c>plan</c> in <c>feature.yaml</c>, <c>spec</c> in an epic file.</summary>
    internal sealed record NamedFile(string Path, string Refusal);

    internal static IEnumerable<NamedFile> Named(WorkFeature feature)
    {
        if (feature.Plan.Length > 0)
        {
            yield return new NamedFile(feature.Plan, WorkPack.NameRefusal("plan", feature.Plan, "feature.yaml", feature.Plan));
        }

        foreach (var epic in feature.AllEpics.Where(epic => epic.Spec.Length > 0))
        {
            var path = $"{epic.Path[..epic.Path.LastIndexOf('/')]}/{epic.Spec}";

            yield return new NamedFile(path, WorkPack.NameRefusal("spec", epic.Spec, epic.Path, path));
        }
    }

    internal static Reading Read(IListedWorkFolder folder, IReadOnlyList<WorkPackOrder.PackFile> files, string featureFolder)
    {
        var entries = folder.Files.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var sections = new List<Section>();
        foreach (var file in files)
        {
            var (section, refusal) = One(folder, entries, file, featureFolder);
            if (section is null)
            {
                return new Reading([], refusal);
            }

            sections.Add(section);
        }

        return new Reading(sections, string.Empty);
    }

    private static (Section? Section, string Refusal) One(IListedWorkFolder folder, Dictionary<string, WorkEntry> entries, WorkPackOrder.PackFile file, string featureFolder)
    {
        var said = file.Key.Length > 0 ? WorkPack.NameRefusal(file.Key, file.Name, file.Where, file.Path) : string.Empty;
        if (said.Length > 0)
        {
            return (null, said);
        }

        var shown = $"{featureFolder}/{file.Path}";
        if (!entries.TryGetValue(file.Path, out var entry))
        {
            return (null, file.Key.Length > 0 ? $"{file.Where}: {file.Key} '{file.Name}' is not a file of the commit — the pack reads only regular files of the feature folder" : $"{shown} is not a file of the commit");
        }

        if (entry.Size > WorkPack.FileBytes)
        {
            return (null, $"{shown} is {entry.Size.ToString(CultureInfo.InvariantCulture)} bytes, over the {WorkPack.FileBytes.ToString(CultureInfo.InvariantCulture)} a file of the pack may be — nothing was cut; make the file smaller");
        }

        return folder.Read(file.Path, WorkPack.FileBytes) switch
        {
            WorkFile.Text text => (new Section(shown, entry.Size, entry.Blob, text.Content), string.Empty),
            WorkFile.TooLarge large => (null, $"{shown} is {large.Bytes.ToString(CultureInfo.InvariantCulture)} bytes, over the {WorkPack.FileBytes.ToString(CultureInfo.InvariantCulture)} a file of the pack may be — nothing was cut"),
            WorkFile.NotUtf8 => (null, $"{shown} is not UTF-8 text — save it as UTF-8 and commit it"),
            _ => (null, $"{shown} is not a file of the commit"),
        };
    }
}
