using System.Text.RegularExpressions;
using System.Text.Json;

namespace CoaiMcp.Core.Toolchain;

/// <summary>The network a default step runs with: the word of <c>shared/toolchains.json</c>.</summary>
public enum DefaultNetwork
{
    /// <summary>Only a loopback device.</summary>
    None,

    /// <summary>The host's network; the step names the heading of a RESULTS file that measured why.</summary>
    Host,
}

/// <summary>The place a measure is written down: a file of this repository and the heading in it.</summary>
public sealed record MeasuredIn(string File, string Heading);

/// <summary>
/// One default step of a stack (<c>shared/toolchains.json</c>): the argv, the network, what it needs from the repository and, for a formatter, the extensions it is given.
/// </summary>
/// <param name="Argv">The command, one item to an argument, started with no shell. Empty when the step has one per runner (<see cref="ByRunner"/>).</param>
/// <param name="Network">The network state of the step.</param>
/// <param name="Needs">The fact that must hold for the step to exist (a sentence; the detector tests the fact).</param>
/// <param name="Fallback">The argv used when the first one's script is not there (a typecheck), or empty.</param>
/// <param name="ByRunner">The argv of each test runner; empty for a step that has one argv.</param>
/// <param name="Extensions">The file extensions a formatter is given; empty for a step that is not one.</param>
/// <param name="MeasuredIn">Where story s1 measured why a step has the host network; null for a step with none.</param>
public sealed record DefaultStep(
    IReadOnlyList<string> Argv,
    DefaultNetwork Network,
    string Needs,
    IReadOnlyList<string> Fallback,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ByRunner,
    IReadOnlyList<string> Extensions,
    MeasuredIn? MeasuredIn);

/// <summary>What means "no test ran" for a runner: exit codes and marker lines (the exit code alone is not enough for VSTest and for the node runners).</summary>
public sealed record NoTestRan(IReadOnlyList<int> ExitCodes, IReadOnlyList<string> Markers, string Note);

/// <summary>A stack: its manifest names (read), its definition files (read: a change of one is a change of the checks), its presence names (recorded, never read: a lock file, a tsconfig) and, for a built one, its default steps.</summary>
public sealed record StackDefaults(
    string Id,
    bool Built,
    IReadOnlyList<string> Manifests,
    IReadOnlyList<string> DefinitionFiles,
    IReadOnlyList<string> Presence,
    IReadOnlyDictionary<string, string> Switches,
    IReadOnlyDictionary<string, DefaultStep?> Steps,
    IReadOnlyDictionary<string, NoTestRan> NoTestRan);

/// <summary>The file did not read: a member nobody knows, a placeholder nobody defined, a wrong type. The message names the path inside the file.</summary>
public sealed class ToolchainDefaultsException(string message) : Exception(message);

/// <summary>
/// The defaults of the detected toolchains (<c>shared/toolchains.json</c>, embedded; epoch 3 E4 s2 t1): the manifest names of the six stacks, and for the two built ones (dotnet and node) the argv of each step, the switch names, the network state of each step, the codes and marker lines that
/// mean no test ran, and the extensions a formatter is given. <b>The binary reads only the embedded copy</b>: nothing here reads a file of a repository or of the data folder.
/// </summary>
/// <remarks>
/// <para><b>The reader is strict.</b> An unknown member, at any level, is a fault; so is a placeholder other than <c>{jobs}</c> and <c>{unit}</c> in an argv item (and <c>{file}</c> in a format step). A fault is thrown with the path of the member: a defaults file that does not read is a build fault, caught by the test that holds the file and the reader equal, never a run-time
/// surprise.</para>
/// <para><b>What is not here:</b> a <c>testFor</c> (there is no default one: a selected run the repository did not name can fail where its own command passes, SPEC decision 4), and anything a repository's own file states.</para>
/// </remarks>
public sealed class ToolchainDefaults
{
    /// <summary>The name of the embedded resource.</summary>
    public const string ResourceName = "CoaiMcp.Core.toolchains.json";

    /// <summary>The placeholders an argv item may hold.</summary>
    public static readonly IReadOnlyList<string> Placeholders = ["{jobs}", "{unit}"];

    /// <summary>The placeholder only a format step's argv may hold.</summary>
    public const string FilePlaceholder = "{file}";

    /// <summary>The names of the steps a stack may define: the five that run in E4.</summary>
    public static readonly IReadOnlyList<string> StepNames = ["restore", "format", "build", "typecheck", "test"];

    private static readonly Lazy<ToolchainDefaults> Shipped = new(() => Parse(ReadSeed()));

    private ToolchainDefaults(IReadOnlyDictionary<string, StackDefaults> stacks, IReadOnlyDictionary<string, IReadOnlyList<string>> frozen, IReadOnlyList<string> allowedSourceExtensions, string configNamePattern, Regex configName)
    {
        Stacks = stacks;
        Frozen = frozen;
        AllowedSourceExtensions = allowedSourceExtensions;
        ConfigNamePattern = configNamePattern;
        ConfigName = configName;
    }

    /// <summary>The frozen build definitions (INTERIM, the owner decides): the patterns of file names, by group, whose change by a coder's head refuses the checks. See <see cref="FrozenDefinitions"/>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Frozen { get; }

    /// <summary>The extensions (lower case, with the dot) of the source files a coder's head may change (the allow list of the interim gate).</summary>
    public IReadOnlyList<string> AllowedSourceExtensions { get; }

    /// <summary>The regular expression (without case) of a configuration's file name: a file whose name matches it is never an allowed source file, whatever its extension.</summary>
    public string ConfigNamePattern { get; }

    /// <summary>The compiled <see cref="ConfigNamePattern"/>, with a timeout.</summary>
    internal Regex ConfigName { get; }

    /// <summary>Every frozen pattern of every group.</summary>
    public IReadOnlyList<string> FrozenPatterns => [.. Frozen.Values.SelectMany(group => group).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The shipped table.</summary>
    public static ToolchainDefaults Current => Shipped.Value;

    /// <summary>Every stack of the file by its id, in the file's order.</summary>
    public IReadOnlyDictionary<string, StackDefaults> Stacks { get; }

    /// <summary>The stacks coai has an adapter for (a default argv); the other stacks are known and refused by name.</summary>
    public IEnumerable<StackDefaults> Built => Stacks.Values.Where(stack => stack.Built);

    /// <summary>The stack ids, in the file's order.</summary>
    public IReadOnlyList<string> Ids => [.. Stacks.Keys];

    /// <summary>Reads the text of a defaults file. Throws <see cref="ToolchainDefaultsException"/> for anything the strict reader does not take.</summary>
    public static ToolchainDefaults Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException e)
        {
            throw new ToolchainDefaultsException($"toolchains.json is not JSON: {e.Message}");
        }

        using (document)
        {
            var root = Members(document.RootElement, "toolchains.json", ["version", "comment", "frozen", "stacks"]);
            if (!root.TryGetValue("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
            {
                throw new ToolchainDefaultsException("toolchains.json: 'version' must be 1");
            }

            var stacks = new Dictionary<string, StackDefaults>(StringComparer.Ordinal);
            foreach (var (id, element) in Members(Required(root, "stacks", "toolchains.json"), "stacks", null))
            {
                stacks[id] = Stack(id, element);
            }

            var frozen = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            IReadOnlyList<string> allowed = [];
            var configPattern = string.Empty;
            if (root.TryGetValue("frozen", out var frozenElement))
            {
                foreach (var (group, names) in Members(frozenElement, "frozen", null))
                {
                    if (group == "comment")
                    {
                        continue;
                    }

                    if (group == "allowedSourceExtensions")
                    {
                        allowed = Strings(names, "frozen.allowedSourceExtensions");
                        if (allowed.Count == 0 || allowed.Any(extension => extension.Length < 2 || extension[0] != '.' || extension.Contains('/') || extension.Contains('\\') || extension != extension.ToLowerInvariant() || extension.IndexOf('.', 1) >= 0))
                        {
                            throw new ToolchainDefaultsException("frozen.allowedSourceExtensions: a list of lower case extensions, each one dot and a name");
                        }

                        continue;
                    }

                    if (group == "configNamePattern")
                    {
                        configPattern = names.ValueKind == JsonValueKind.String ? names.GetString() ?? string.Empty : throw new ToolchainDefaultsException("frozen.configNamePattern: a text was expected");

                        continue;
                    }

                    frozen[group] = Strings(names, $"frozen.{group}");
                    if (frozen[group].Any(pattern => pattern.Length == 0 || pattern.Contains('\\') || pattern.StartsWith('/') || pattern.EndsWith('/') || pattern.Contains("//", StringComparison.Ordinal)))
                    {
                        throw new ToolchainDefaultsException($"frozen.{group}: a pattern is a non-empty name or a few names joined by '/', with no backslash and no empty segment");
                    }
                }
            }

            Regex configName;
            try
            {
                configName = new Regex(configPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                throw new ToolchainDefaultsException("frozen.configNamePattern: a regular expression was expected");
            }

            if (allowed.Count == 0 || configPattern.Length == 0)
            {
                throw new ToolchainDefaultsException("frozen: 'allowedSourceExtensions' and 'configNamePattern' are both required");
            }

            return new ToolchainDefaults(stacks, frozen, allowed, configPattern, configName);
        }
    }

    /// <summary>The manifest names and definition files of every stack, flattened — what the search for manifests looks for.</summary>
    public IReadOnlyList<string> AllManifestNames => [.. Stacks.Values.SelectMany(stack => stack.Manifests.Concat(stack.DefinitionFiles).Concat(stack.Presence)).Distinct(StringComparer.Ordinal)];

    private static StackDefaults Stack(string id, JsonElement element)
    {
        var where = $"stacks.{id}";
        var members = Members(element, where, ["built", "manifests", "definitionFiles", "presence", "switches", "steps", "noTestRan"]);
        var built = Required(members, "built", where).ValueKind == JsonValueKind.True;
        if (!built && members.Keys.Any(name => name is not ("built" or "manifests")))
        {
            throw new ToolchainDefaultsException($"{where}: a stack that is not built has only 'built' and 'manifests'");
        }

        var steps = new Dictionary<string, DefaultStep?>(StringComparer.Ordinal);
        if (members.TryGetValue("steps", out var stepsElement))
        {
            foreach (var (name, step) in Members(stepsElement, $"{where}.steps", StepNames))
            {
                steps[name] = step.ValueKind == JsonValueKind.Null ? null : Step(name, step, $"{where}.steps.{name}");
            }
        }

        var noTest = new Dictionary<string, NoTestRan>(StringComparer.Ordinal);
        if (members.TryGetValue("noTestRan", out var noTestElement))
        {
            foreach (var (runner, row) in Members(noTestElement, $"{where}.noTestRan", null))
            {
                var fields = Members(row, $"{where}.noTestRan.{runner}", ["exitCodes", "markers", "note"]);
                noTest[runner] = new NoTestRan(
                    [.. Items(Required(fields, "exitCodes", $"{where}.noTestRan.{runner}"), $"{where}.noTestRan.{runner}.exitCodes").Select(code => code.TryGetInt32(out var value) ? value : throw new ToolchainDefaultsException($"{where}.noTestRan.{runner}.exitCodes: a whole number was expected"))],
                    Strings(Required(fields, "markers", $"{where}.noTestRan.{runner}"), $"{where}.noTestRan.{runner}.markers"),
                    fields.TryGetValue("note", out var note) ? note.GetString() ?? string.Empty : string.Empty);
            }
        }

        var switches = new Dictionary<string, string>(StringComparer.Ordinal);
        if (members.TryGetValue("switches", out var switchElement))
        {
            foreach (var (name, value) in Members(switchElement, $"{where}.switches", null))
            {
                switches[name] = value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new ToolchainDefaultsException($"{where}.switches.{name}: a text was expected");
            }
        }

        return new StackDefaults(
            id,
            built,
            Strings(Required(members, "manifests", where), $"{where}.manifests"),
            members.TryGetValue("definitionFiles", out var definition) ? Strings(definition, $"{where}.definitionFiles") : [],
            members.TryGetValue("presence", out var presence) ? Strings(presence, $"{where}.presence") : [],
            switches,
            steps,
            noTest);
    }

    private static DefaultStep Step(string name, JsonElement element, string where)
    {
        var members = Members(element, where, ["argv", "fallback", "byRunner", "network", "needs", "extensions", "measuredIn"]);
        var isFormat = name == "format";
        var argv = members.TryGetValue("argv", out var argvElement) ? Argv(argvElement, $"{where}.argv", isFormat) : [];
        var fallback = members.TryGetValue("fallback", out var fallbackElement) ? Argv(fallbackElement, $"{where}.fallback", isFormat) : [];
        var byRunner = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (members.TryGetValue("byRunner", out var runners))
        {
            foreach (var (runner, runnerArgv) in Members(runners, $"{where}.byRunner", null))
            {
                byRunner[runner] = Argv(runnerArgv, $"{where}.byRunner.{runner}", isFormat);
            }
        }

        if (argv.Count == 0 && byRunner.Count == 0)
        {
            throw new ToolchainDefaultsException($"{where}: a step has an 'argv' or a 'byRunner'");
        }

        if (argv.Count > 0 && byRunner.Count > 0)
        {
            throw new ToolchainDefaultsException($"{where}: a step has an 'argv' or a 'byRunner', not both");
        }

        var network = Required(members, "network", where).GetString() switch
        {
            "none" => DefaultNetwork.None,
            "host" => DefaultNetwork.Host,
            var other => throw new ToolchainDefaultsException($"{where}.network: 'none' or 'host' was expected, not '{other}'"),
        };
        var measured = members.TryGetValue("measuredIn", out var measuredElement) ? MeasuredInOf(measuredElement, $"{where}.measuredIn") : null;
        if (network == DefaultNetwork.Host && measured is null)
        {
            throw new ToolchainDefaultsException($"{where}: a step with the host network names the RESULTS heading that measured why ('measuredIn')");
        }

        var extensions = members.TryGetValue("extensions", out var extensionElement) ? Strings(extensionElement, $"{where}.extensions") : [];
        if (isFormat != (extensions.Count > 0))
        {
            throw new ToolchainDefaultsException($"{where}: 'extensions' belongs to a format step and to nothing else");
        }

        return new DefaultStep(argv, network, members.TryGetValue("needs", out var needs) ? needs.GetString() ?? string.Empty : string.Empty, fallback, byRunner, extensions, measured);
    }

    private static MeasuredIn MeasuredInOf(JsonElement element, string where)
    {
        var members = Members(element, where, ["file", "heading"]);

        return new MeasuredIn(Required(members, "file", where).GetString() ?? string.Empty, Required(members, "heading", where).GetString() ?? string.Empty);
    }

    private static IReadOnlyList<string> Argv(JsonElement element, string where, bool isFormat)
    {
        var items = Strings(element, where);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var stripped = item;
            foreach (var placeholder in isFormat ? Placeholders.Append(FilePlaceholder).ToArray() : Placeholders.ToArray())
            {
                stripped = stripped.Replace(placeholder, string.Empty, StringComparison.Ordinal);
            }

            if (stripped.Contains('{', StringComparison.Ordinal) || stripped.Contains('}', StringComparison.Ordinal))
            {
                throw new ToolchainDefaultsException($"{where}[{index}]: the item '{item}' holds a placeholder nobody defined (only {string.Join(", ", Placeholders)}{(isFormat ? ", " + FilePlaceholder : string.Empty)} are)");
            }

            if (item.Contains(FilePlaceholder, StringComparison.Ordinal) && item != FilePlaceholder)
            {
                throw new ToolchainDefaultsException($"{where}[{index}]: {FilePlaceholder} must be an item of its own: it is replaced by one item per file");
            }
        }

        return items;
    }

    private static Dictionary<string, JsonElement> Members(JsonElement element, string where, IReadOnlyList<string>? allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ToolchainDefaultsException($"{where}: an object was expected");
        }

        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (allowed is not null && !allowed.Contains(property.Name))
            {
                throw new ToolchainDefaultsException($"{where}: the member '{property.Name}' is not known (known: {string.Join(", ", allowed)})");
            }

            if (!members.TryAdd(property.Name, property.Value))
            {
                throw new ToolchainDefaultsException($"{where}: the member '{property.Name}' is there twice");
            }
        }

        return members;
    }

    private static JsonElement Required(Dictionary<string, JsonElement> members, string name, string where) =>
        members.TryGetValue(name, out var value) ? value : throw new ToolchainDefaultsException($"{where}: the member '{name}' is missing");

    private static IEnumerable<JsonElement> Items(JsonElement element, string where) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : throw new ToolchainDefaultsException($"{where}: a list was expected");

    private static List<string> Strings(JsonElement element, string where) =>
        [.. Items(element, where).Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new ToolchainDefaultsException($"{where}: a list of texts was expected"))];

    private static string ReadSeed()
    {
        using var stream = typeof(ToolchainDefaults).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the embedded resource {ResourceName} is missing from the build");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
