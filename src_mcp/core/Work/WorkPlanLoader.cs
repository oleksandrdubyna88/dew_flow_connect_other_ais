namespace CoaiMcp.Core.Work;

/// <summary>
/// Reads a feature folder into a <see cref="WorkFeature"/>: walks the folders by their names, reads each item's file through the <see cref="IYamlReader"/>, checks its shape
/// (<see cref="WorkShape"/>) and that its folder is its id. Every fault is added to <c>errors</c>; a file with a fault is read as far as it can be and marked not loaded.
/// </summary>
internal sealed class WorkPlanLoader(IWorkFolder folder, IYamlReader reader, List<WorkError> errors)
{
    private const string StoriesFolder = "stories";
    private const string TasksFolder = "tasks";

    /// <summary>The feature, or null when there is no <c>feature.yaml</c> (a missing-file error says so and nothing else is read).</summary>
    internal WorkFeature? Load()
    {
        var read = Read(WorkTables.Feature.File, string.Empty, WorkTables.Feature, string.Empty);
        if (!read.Found)
        {
            return null;
        }

        var map = read.Map;
        var epochs = WorkNames.InOrder(folder.Folders(string.Empty).Where(WorkNames.IsEpoch)).Select(LoadEpoch).ToList();

        return new WorkFeature(map is not null, Strings(map, "epochs"), MethodNumbers(map), epochs);
    }

    // ---------- items ----------

    private WorkEpoch LoadEpoch(string name)
    {
        var read = Read($"{name}/{WorkTables.Epoch.File}", name, WorkTables.Epoch, name);
        var ok = read.Map is not null && IdMatches(read.Map, name, $"{name}/{WorkTables.Epoch.File}", name);
        var epics = WorkNames.InOrder(folder.Folders(name).Where(WorkNames.IsEpic)).Select(epic => LoadEpic(name, epic)).ToList();

        return new WorkEpoch(name, name, $"{name}/{WorkTables.Epoch.File}", ok, Strings(read.Map, "dependsOn"), Strings(read.Map, "epics"), epics);
    }

    private WorkEpic LoadEpic(string epoch, string name)
    {
        var path = $"{epoch}/{name}";
        var node = $"{epoch}/{name}";
        var read = Read($"{path}/{WorkTables.Epic.File}", node, WorkTables.Epic, node);
        var ok = read.Map is not null && IdMatches(read.Map, name, $"{path}/{WorkTables.Epic.File}", node);
        var stories = WorkNames.InOrder(folder.Folders($"{path}/{StoriesFolder}").Where(WorkNames.IsStory)).Select(story => LoadStory(path, node, story)).ToList();

        return new WorkEpic(name, node, $"{path}/{WorkTables.Epic.File}", ok, ok && Boolean(read.Map, "mvp"), Strings(read.Map, "dependsOn"), stories);
    }

    private WorkStory LoadStory(string epicPath, string epicNode, string name)
    {
        var path = $"{epicPath}/{StoriesFolder}/{name}";
        var node = $"{epicNode}/{name}";
        var read = Read($"{path}/{WorkTables.Story.File}", node, WorkTables.Story, node);
        var ok = read.Map is not null && IdMatches(read.Map, name, $"{path}/{WorkTables.Story.File}", node);
        var tasks = WorkNames.InOrder(folder.Folders($"{path}/{TasksFolder}").Where(WorkNames.IsTask)).Select(task => LoadTask(path, node, task)).ToList();

        return new WorkStory(name, node, $"{path}/{WorkTables.Story.File}", ok, Strings(read.Map, "dependsOn"), tasks);
    }

    private WorkTask LoadTask(string storyPath, string storyNode, string name)
    {
        var path = $"{storyPath}/{TasksFolder}/{name}/{WorkTables.Task.File}";
        var node = $"{storyNode}/{name}";
        var read = Read(path, node, WorkTables.Task, node);
        var ok = read.Map is not null && IdMatches(read.Map, name, path, node);
        var map = read.Map;

        return new WorkTask(
            name, node, path, ok, Text(map, "title"), Text(map, "kind"), (int)Integer(map, "complexity"), Strings(map, "dependsOn"), Boolean(map, "parallel"),
            Strings(map, "files"), Dod(map), SecurityOf(map));
    }

    /// <summary>The folder is the id: a file whose id says another name is an error, and the node is then treated as not loaded.</summary>
    private bool IdMatches(YamlMap map, string name, string path, string node)
    {
        if (map.Find("id") is { Value: YamlScalar { Kind: YamlScalarKind.String } id } && id.Text != name)
        {
            errors.Add(new WorkError(WorkErrorCodes.FolderIdMismatch, path, node, $"line {id.Line}: the folder is '{name}' but the id is '{id.Text}'; the folder name is the id"));

            return false;
        }

        return true;
    }

    // ---------- files ----------

    /// <summary>One file read: whether it exists, and its map when it read clean.</summary>
    private readonly record struct FileRead(bool Found, YamlMap? Map);

    private FileRead Read(string path, string node, WorkLevel level, string where)
    {
        var raw = folder.Read(path, StrictYamlReader.MaxBytes);
        switch (raw)
        {
            case WorkFile.Missing:
                errors.Add(new WorkError(WorkErrorCodes.MissingFile, path, node, $"{level.File} is missing from {(where.Length == 0 ? "the feature folder" : where)}"));

                return new FileRead(false, null);
            case WorkFile.TooLarge large:
                errors.Add(new WorkError(WorkErrorCodes.YamlTooLarge, path, node, $"the file is {large.Bytes} bytes; at most {StrictYamlReader.MaxBytes} are read"));

                return new FileRead(true, null);
            case WorkFile.NotUtf8:
                errors.Add(new WorkError(WorkErrorCodes.YamlSyntax, path, node, "the file is not UTF-8 text"));

                return new FileRead(true, null);
            default:
                return Parse(((WorkFile.Text)raw).Content, path, node, level);
        }
    }

    private FileRead Parse(string text, string path, string node, WorkLevel level)
    {
        switch (reader.Read(text))
        {
            case YamlRead.Refused refused:
                errors.Add(new WorkError(refused.Code, path, node, refused.Message));

                return new FileRead(true, null);
            case YamlRead.Parsed parsed:
                return new FileRead(true, WorkShape.Clean(parsed.Root, level, path, node, errors) ? (YamlMap)parsed.Root : null);
            default:
                return new FileRead(true, null);
        }
    }

    // ---------- values of a clean file; an unread file gives empty ones ----------

    private static string Text(YamlMap? map, string key) => map?.Find(key)?.Value is YamlScalar scalar ? scalar.Text : string.Empty;

    private static bool Boolean(YamlMap? map, string key) => map?.Find(key)?.Value is YamlScalar { Kind: YamlScalarKind.Boolean } scalar && scalar.Boolean;

    private static long Integer(YamlMap? map, string key) => map?.Find(key)?.Value is YamlScalar { Kind: YamlScalarKind.Integer } scalar ? scalar.Integer : 0;

    private static IReadOnlyList<string> Strings(YamlMap? map, string key) =>
        map?.Find(key)?.Value is YamlList list ? [.. list.Items.OfType<YamlScalar>().Select(item => item.Text)] : [];

    private static SecurityFlag SecurityOf(YamlMap? map) =>
        map?.Find("security")?.Value is YamlScalar { Kind: YamlScalarKind.Boolean } flag ? (flag.Boolean ? SecurityFlag.True : SecurityFlag.False) : SecurityFlag.Absent;

    private static IReadOnlyList<WorkDod> Dod(YamlMap? map) =>
        map?.Find("dod")?.Value is YamlList list
            ? [.. list.Items.OfType<YamlMap>().Select(item => new WorkDod(Text(item, "id"), Text(item, "item")))]
            : [];

    private static IReadOnlyDictionary<string, long> MethodNumbers(YamlMap? feature)
    {
        var numbers = new Dictionary<string, long>(StringComparer.Ordinal);
        if (feature?.Find("workMethod")?.Value is not YamlMap method)
        {
            return numbers;
        }

        foreach (var entry in method.Entries)
        {
            if (entry.Value is YamlScalar { Kind: YamlScalarKind.Integer } number)
            {
                numbers[entry.Key] = number.Integer;
            }
            else if (entry.Value is YamlMap range)
            {
                foreach (var inner in range.Entries.Where(inner => inner.Value is YamlScalar { Kind: YamlScalarKind.Integer }))
                {
                    numbers[$"{entry.Key}.{inner.Key}"] = ((YamlScalar)inner.Value).Integer;
                }
            }
        }

        return numbers;
    }
}
