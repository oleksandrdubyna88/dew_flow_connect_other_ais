namespace CoaiMcp.Core.Work;

/// <summary>
/// Checks one parsed file against its <see cref="WorkLevel"/>: the type of every key, the keys that must be there, the keys that must not, and the value rules a schema states
/// (id patterns, the kind and status lists, the complexity range, a <c>files</c> entry that stays inside the repository). It answers whether the file was clean; every fault is an error with
/// its code (<c>wrong-type</c>, <c>missing-key</c>, <c>unknown-key</c>, <c>bad-id</c>, <c>bad-value</c>).
/// </summary>
internal sealed class WorkShape(WorkLevel level, string path, string node, List<WorkError> errors)
{
    private readonly int _before = errors.Count;

    /// <summary>True when the file raised no error.</summary>
    internal static bool Clean(YamlNode root, WorkLevel level, string path, string node, List<WorkError> errors)
    {
        var shape = new WorkShape(level, path, node, errors);
        if (root is not YamlMap map)
        {
            shape.Add(WorkErrorCodes.WrongType, $"{level.File} must be a map of keys, not {Describe(root)}");

            return false;
        }

        shape.Keys(map);

        return errors.Count == shape._before;
    }

    private void Keys(YamlMap map)
    {
        if (!level.AllowUnknown)
        {
            foreach (var entry in map.Entries.Where(entry => !level.Fields.ContainsKey(entry.Key)))
            {
                Add(WorkErrorCodes.UnknownKey, $"line {entry.Line}: '{entry.Key}' is not a key of a {level.Name} (the keys are {string.Join(", ", level.Fields.Keys)})");
            }
        }

        foreach (var required in level.Required.Where(key => map.Find(key) is null))
        {
            Add(WorkErrorCodes.MissingKey, $"the key '{required}' is missing from the {level.Name}");
        }

        foreach (var entry in map.Entries.Where(entry => level.Fields.ContainsKey(entry.Key)))
        {
            Field(entry, level.Fields[entry.Key]);
        }
    }

    private void Field(YamlEntry entry, WorkField field)
    {
        if (!TypeIs(entry.Value, field))
        {
            Add(WorkErrorCodes.WrongType, $"line {entry.Line}: '{entry.Key}' must be {Expected(field)}, not {Describe(entry.Value)}");

            return;
        }

        Value(entry);
    }

    private static bool TypeIs(YamlNode node, WorkField field) => field switch
    {
        WorkField.Text => node is YamlScalar { Kind: YamlScalarKind.String },
        WorkField.Boolean => node is YamlScalar { Kind: YamlScalarKind.Boolean },
        WorkField.Integer => node is YamlScalar { Kind: YamlScalarKind.Integer },
        WorkField.IdList => node is YamlList list && list.Items.All(item => item is YamlScalar { Kind: YamlScalarKind.String }),
        WorkField.Map => node is YamlMap,
        _ => node is YamlList dod && dod.Items.All(item => item is YamlMap),
    };

    private static string Expected(WorkField field) => field switch
    {
        WorkField.Text => "a string (a quoted value is always a string)",
        WorkField.Boolean => "true or false (a plain yes or no is a string)",
        WorkField.Integer => "a whole number (a quoted number is a string)",
        WorkField.IdList => "a list of strings",
        WorkField.Map => "a map",
        _ => "a list of maps {id, item}",
    };

    internal static string Describe(YamlNode node) => node switch
    {
        YamlMap => "a map",
        YamlList => "a list",
        YamlScalar { Kind: YamlScalarKind.Null } => "empty",
        YamlScalar { Kind: YamlScalarKind.Integer } => "a whole number",
        YamlScalar { Kind: YamlScalarKind.Boolean } => "true or false",
        _ => "a string",
    };

    // ---------- values ----------

    private void Value(YamlEntry entry)
    {
        switch (entry.Key)
        {
            case "id":
                Id(entry, level.IdPattern, "id");
                break;
            case "title":
                NonEmpty(entry);
                break;
            case "kind" when !WorkTables.Kinds.Contains(Text(entry)):
                Add(WorkErrorCodes.BadValue, $"line {entry.Line}: kind '{Text(entry)}' is not one of {string.Join(", ", WorkTables.Kinds)}");
                break;
            case "status" when !WorkTables.Statuses.Contains(Text(entry)):
                Add(WorkErrorCodes.BadValue, $"line {entry.Line}: status '{Text(entry)}' is not one of {string.Join(", ", WorkTables.Statuses)}");
                break;
            case "complexity" when ((YamlScalar)entry.Value).Integer is < WorkTables.ComplexityMin or > WorkTables.ComplexityMax:
                Add(WorkErrorCodes.BadValue, $"line {entry.Line}: complexity {((YamlScalar)entry.Value).Integer} is outside {WorkTables.ComplexityMin}..{WorkTables.ComplexityMax}");
                break;
            case "files":
                Files((YamlList)entry.Value);
                break;
            case "dod":
                Dod((YamlList)entry.Value);
                break;
            default:
                break;
        }
    }

    private static string Text(YamlEntry entry) => ((YamlScalar)entry.Value).Text;

    private void Id(YamlEntry entry, string pattern, string what)
    {
        if (pattern.Length > 0 && !WorkTables.IsId(pattern, Text(entry)))
        {
            Add(WorkErrorCodes.BadId, $"line {entry.Line}: the {what} '{Shorten(Text(entry))}' does not match {pattern} in full");
        }
        else if (pattern.Length == 0)
        {
            NonEmpty(entry);
        }
    }

    private void NonEmpty(YamlEntry entry)
    {
        if (Text(entry).Trim().Length == 0)
        {
            Add(WorkErrorCodes.BadValue, $"line {entry.Line}: '{entry.Key}' is empty");
        }
    }

    private void Files(YamlList files)
    {
        foreach (var file in files.Items.Cast<YamlScalar>().Where(file => !RepoPaths.IsRelative(file.Text)))
        {
            Add(WorkErrorCodes.BadValue, $"line {file.Line}: the files entry '{Shorten(file.Text)}' is not a path inside the repository");
        }
    }

    private void Dod(YamlList dod)
    {
        foreach (var item in dod.Items.Cast<YamlMap>())
        {
            foreach (var entry in item.Entries.Where(entry => entry.Key is not ("id" or "item")))
            {
                Add(WorkErrorCodes.UnknownKey, $"line {entry.Line}: '{entry.Key}' is not a key of a dod item (the keys are id, item)");
            }

            DodKey(item, "id", WorkTables.DodIdPattern);
            DodKey(item, "item", string.Empty);
        }
    }

    private void DodKey(YamlMap item, string key, string pattern)
    {
        if (item.Find(key) is not { } entry)
        {
            Add(WorkErrorCodes.MissingKey, $"line {item.Line}: a dod item has no '{key}'");
        }
        else if (!TypeIs(entry.Value, WorkField.Text))
        {
            Add(WorkErrorCodes.WrongType, $"line {entry.Line}: a dod '{key}' must be a string, not {Describe(entry.Value)}");
        }
        else
        {
            Id(entry, pattern, "dod id");
        }
    }

    private void Add(string code, string message) => errors.Add(new WorkError(code, path, node, message));

    private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "...";
}
