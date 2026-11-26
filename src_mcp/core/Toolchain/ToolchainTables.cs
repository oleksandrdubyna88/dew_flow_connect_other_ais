namespace CoaiMcp.Core.Toolchain;

/// <summary>The type of a key's value in the toolchain file.</summary>
public enum ToolchainField
{
    Integer,
    Text,
    Argv,
    SkipList,
    TextList,
    Projects,
    Timeouts,
    Instructions,
}

/// <summary>One map of the file: its keys with their types, and the keys that must be there.</summary>
public sealed record ToolchainLevel(string Name, IReadOnlyDictionary<string, ToolchainField> Fields, IReadOnlyList<string> Required);

/// <summary>
/// The tables of the toolchain file: the maps, their keys, the adapters, the steps a repository may skip and the limits. ONE source for the reader;
/// <c>shared/toolchain.schema.json</c> says the same in JSON Schema and <c>AToolchainSchemaAgreesWithTheReaderTests</c> holds the two equal — neither half has a schema engine.
/// </summary>
public static class ToolchainTables
{
    /// <summary>Where the file is, from the repository root.</summary>
    public const string FilePath = ".coai/toolchain.yaml";

    /// <summary>The only version there is.</summary>
    public const int Version = 1;

    /// <summary>The largest file read: the YAML reader's own limit, so a bigger file is refused by name before it is parsed.</summary>
    public const int MaxBytes = Work.StrictYamlReader.MaxBytes;

    /// <summary>The placeholder an item of <c>instructions.perFile</c> may hold: replaced by each file of the task.</summary>
    public const string FilePlaceholder = "{file}";

    /// <summary>The range of <c>instructions.timeoutSeconds</c>, and what a file that names none gets.</summary>
    public const int InstructionsTimeoutMin = 1;

    public const int InstructionsTimeoutMax = 300;

    public const int InstructionsTimeoutDefault = 60;

    public const int MemoryMbMin = 1;

    public const int MemoryMbMax = 1_048_576;

    /// <summary>A duration is a whole number and a unit: <c>90s</c>, <c>15m</c>, <c>2h</c>.</summary>
    public const string DurationPattern = "^[1-9][0-9]*[smh]$";

    public static readonly TimeSpan DefaultBuildTimeout = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan DefaultTestTimeout = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan MaxTimeout = TimeSpan.FromHours(24);

    /// <summary>The stacks an adapter exists for (server.md § 12). The adapter names the parsers a failed step is read with.</summary>
    public static readonly IReadOnlyList<string> Adapters = ["dotnet", "node", "php", "rust", "python", "java"];

    /// <summary>
    /// The steps a repository may leave out. NOT <c>build</c> and NOT <c>test</c>: a file that skips them would be green having checked nothing.
    /// </summary>
    public static readonly IReadOnlyList<string> SkippableSteps = ["restore", "lint", "format", "formatCheck", "audit", "coverage"];

    public static readonly ToolchainLevel Root = new(
        "toolchain",
        new Dictionary<string, ToolchainField>
        {
            ["version"] = ToolchainField.Integer,
            ["projects"] = ToolchainField.Projects,
            ["skip"] = ToolchainField.SkipList,
            ["timeouts"] = ToolchainField.Timeouts,
            ["instructions"] = ToolchainField.Instructions,
        },
        ["version", "projects"]);

    public static readonly ToolchainLevel Project = new(
        "project",
        new Dictionary<string, ToolchainField>
        {
            ["path"] = ToolchainField.Text,
            ["adapter"] = ToolchainField.Text,
            ["build"] = ToolchainField.Argv,
            ["test"] = ToolchainField.Argv,
            ["restore"] = ToolchainField.Argv,
            ["format"] = ToolchainField.Argv,
            ["testFor"] = ToolchainField.Argv,
            ["testFiles"] = ToolchainField.TextList,
            ["memoryMb"] = ToolchainField.Integer,
        },
        ["path", "adapter"]);

    public static readonly ToolchainLevel Timeouts = new(
        "timeouts",
        new Dictionary<string, ToolchainField> { ["build"] = ToolchainField.Text, ["test"] = ToolchainField.Text },
        []);

    public static readonly ToolchainLevel Instructions = new(
        "instructions",
        new Dictionary<string, ToolchainField> { ["argv"] = ToolchainField.Argv, ["perFile"] = ToolchainField.Argv, ["timeoutSeconds"] = ToolchainField.Integer },
        ["argv"]);

    public static readonly IReadOnlyList<ToolchainLevel> Levels = [Root, Project, Timeouts, Instructions];
}
