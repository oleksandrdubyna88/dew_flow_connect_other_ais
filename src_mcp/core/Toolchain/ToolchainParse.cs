using System.Globalization;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Work;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// One read of a parsed toolchain file: the faults of every key are collected, none stops the read, and a file with a fault is <see cref="ToolchainRead.Refused"/> whatever the values built on the way were.
/// A fault always names the key (<c>'build'</c>) and the line.
/// </summary>
internal sealed partial class ToolchainParse
{
    private readonly List<string> _faults = [];

    internal static ToolchainRead Run(YamlNode root)
    {
        var parse = new ToolchainParse();
        var toolchain = root is YamlMap map ? parse.Whole(map) : parse.NotAMap(root);

        return parse._faults.Count == 0 ? new ToolchainRead.Configured(toolchain) : new ToolchainRead.Refused([.. parse._faults]);
    }

    private void Fault(string sentence) => _faults.Add(sentence);

    private RepoToolchain NotAMap(YamlNode root)
    {
        Fault($"the toolchain file must be a map of keys ({string.Join(", ", ToolchainTables.Root.Fields.Keys)}), not {WorkShape.Describe(root)}");

        return new RepoToolchain([], [], ToolchainTimeouts.Default);
    }

    private RepoToolchain Whole(YamlMap map)
    {
        Keys(map, ToolchainTables.Root, ToolchainTables.Root.Name);
        Version(map);

        return new RepoToolchain(Projects(map), Skip(map), Timeouts(map));
    }

    /// <summary>The keys of a map: one fault for each that is not in the table and one for each required key that is not there.</summary>
    private void Keys(YamlMap map, ToolchainLevel level, string where)
    {
        foreach (var entry in map.Entries.Where(entry => !level.Fields.ContainsKey(entry.Key)))
        {
            Fault($"line {entry.Line}: '{entry.Key}' is not a key of {where} (the keys are {string.Join(", ", level.Fields.Keys)})");
        }

        foreach (var required in level.Required.Where(key => map.Find(key) is null))
        {
            Fault($"the key '{required}' is missing from {where}");
        }
    }

    /// <summary>
    /// What a node is, for a sentence: its kind, and for a string its length — NEVER its text. A person may paste anything into this file, a token included, and a refusal is printed and recorded; only the keys of the file (names the person chose for the structure)
    /// and the legal values are ever spelled.
    /// </summary>
    private static string Shown(YamlNode node) => node is YamlScalar { Kind: YamlScalarKind.String } scalar ? $"a string of {scalar.Text.Length} characters" : WorkShape.Describe(node);

    /// <summary>What stops a value being a list of strings — the first item that is not one, or the value itself — or empty when it is one.</summary>
    private static string NotStrings(YamlNode value) => value switch
    {
        YamlList list => list.Items.FirstOrDefault(item => item is not YamlScalar { Kind: YamlScalarKind.String }) is { } bad ? Shown(bad) : string.Empty,
        _ => Shown(value),
    };

    private void Version(YamlMap map)
    {
        if (map.Find("version") is { } entry && entry.Value is not YamlScalar { Kind: YamlScalarKind.Integer, Integer: ToolchainTables.Version })
        {
            Fault($"line {entry.Line}: 'version' must be the whole number {ToolchainTables.Version}, not {Shown(entry.Value)}");
        }
    }

    // ---------- projects ----------

    private List<ToolchainProject> Projects(YamlMap map)
    {
        if (map.Find("projects") is not { } entry)
        {
            return [];
        }

        if (entry.Value is not YamlList { Items.Count: > 0 } list)
        {
            Fault($"line {entry.Line}: 'projects' must be a list of at least one project, not {Shown(entry.Value)}");

            return [];
        }

        var projects = list.Items.Select((item, index) => Project(item, index + 1)).ToList();
        foreach (var path in projects.Where(project => project.Path.Length > 0).GroupBy(project => project.Path, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            Fault($"line {entry.Line}: {path.Count()} projects name the same 'path'");
        }

        return projects;
    }

    private ToolchainProject Project(YamlNode node, int number)
    {
        var where = $"project {number}";
        if (node is not YamlMap map)
        {
            Fault($"line {node.Line}: {where} must be a map of keys (the keys are {string.Join(", ", ToolchainTables.Project.Fields.Keys)}), not {Shown(node)}");

            return new ToolchainProject(string.Empty, string.Empty, ToolchainCommand.NotStated, ToolchainCommand.NotStated, null);
        }

        Keys(map, ToolchainTables.Project, where);

        return new ToolchainProject(Path(map, where), Adapter(map, where), Command(map, "build", where), Command(map, "test", where), Memory(map, where));
    }

    private string Path(YamlMap map, string where)
    {
        if (!Text(map, "path", where, out var entry, out var text))
        {
            return string.Empty;
        }

        var canonical = RepoPaths.Canonical(text);
        if (RepoPaths.WhyNotRelative(canonical) is { Length: > 0 } why)
        {
            Fault($"line {entry.Line}: 'path' of {where} {why}; it must name a folder inside the repository");
        }

        return canonical;
    }

    private string Adapter(YamlMap map, string where)
    {
        if (!Text(map, "adapter", where, out var entry, out var text))
        {
            return string.Empty;
        }

        if (!ToolchainTables.Adapters.Contains(text))
        {
            Fault($"line {entry.Line}: 'adapter' of {where} is not one of the adapters (the value is a string of {text.Length} characters); the adapters are {string.Join(", ", ToolchainTables.Adapters)}");
        }

        return text;
    }

    /// <summary>A key that must be a string: false when it is absent (the key check said so) or is something else (said here).</summary>
    private bool Text(YamlMap map, string key, string where, out YamlEntry entry, out string text)
    {
        entry = map.Find(key)!;
        text = string.Empty;
        if (entry is null)
        {
            return false;
        }

        if (entry.Value is not YamlScalar { Kind: YamlScalarKind.String } scalar)
        {
            Fault($"line {entry.Line}: '{key}' of {where} must be a string, not {Shown(entry.Value)}");

            return false;
        }

        text = scalar.Text;

        return true;
    }

    private int? Memory(YamlMap map, string where)
    {
        if (map.Find("memoryMb") is not { } entry)
        {
            return null;
        }

        if (entry.Value is YamlScalar { Kind: YamlScalarKind.Integer } number && number.Integer is >= ToolchainTables.MemoryMbMin and <= ToolchainTables.MemoryMbMax)
        {
            return (int)number.Integer;
        }

        Fault($"line {entry.Line}: 'memoryMb' of {where} must be a whole number from {ToolchainTables.MemoryMbMin} to {ToolchainTables.MemoryMbMax}, not {Shown(entry.Value)}");

        return null;
    }

    // ---------- commands ----------

    private ToolchainCommand Command(YamlMap map, string key, string where)
    {
        if (map.Find(key) is not { } entry)
        {
            return ToolchainCommand.NotStated;
        }

        if (NotStrings(entry.Value) is { Length: > 0 } shown)
        {
            Fault($"line {entry.Line}: '{key}' of {where} must be a list of strings, one item to a line (a number or true/false is written in quotes), not {shown}");

            return ToolchainCommand.NotStated;
        }

        var argv = ((YamlList)entry.Value).Items.Cast<YamlScalar>().Select(item => item.Text).ToList();
        NoSecret(argv, entry, key, where);

        return new ToolchainCommand(true, argv);
    }

    /// <summary>A secret never goes in argv: it shows in the process list and in every record of the step. The fault names the class and never repeats the text.</summary>
    private void NoSecret(List<string> argv, YamlEntry entry, string key, string where)
    {
        foreach (var kind in argv.Select(Redaction.FirstSecretClass).Where(kind => kind.Length > 0))
        {
            Fault($"line {entry.Line}: '{key}' of {where} holds text that looks like a credential ({kind}); a secret never goes in argv");
        }
    }

    // ---------- skip and timeouts ----------

    private List<string> Skip(YamlMap map)
    {
        if (map.Find("skip") is not { } entry)
        {
            return [];
        }

        if (NotStrings(entry.Value) is { Length: > 0 } shown)
        {
            Fault($"line {entry.Line}: 'skip' must be a list of strings, one item to a line, not {shown}");

            return [];
        }

        var steps = ((YamlList)entry.Value).Items.Cast<YamlScalar>().Select(item => item.Text).ToList();
        foreach (var step in steps.Where(step => !ToolchainTables.SkippableSteps.Contains(step)))
        {
            Fault($"line {entry.Line}: 'skip' names a step that cannot be skipped (a string of {step.Length} characters); the steps that can be skipped are {string.Join(", ", ToolchainTables.SkippableSteps)} (build and test cannot)");
        }

        return steps;
    }

    private ToolchainTimeouts Timeouts(YamlMap map)
    {
        if (map.Find("timeouts") is not { } entry)
        {
            return ToolchainTimeouts.Default;
        }

        if (entry.Value is not YamlMap timeouts)
        {
            Fault($"line {entry.Line}: 'timeouts' must be a map of keys (build, test), not {Shown(entry.Value)}");

            return ToolchainTimeouts.Default;
        }

        Keys(timeouts, ToolchainTables.Timeouts, "timeouts");

        return new ToolchainTimeouts(Duration(timeouts, "build", ToolchainTimeouts.Default.Build), Duration(timeouts, "test", ToolchainTimeouts.Default.Test));
    }

    private TimeSpan Duration(YamlMap timeouts, string key, TimeSpan fallback)
    {
        if (timeouts.Find(key) is not { } entry)
        {
            return fallback;
        }

        if (entry.Value is YamlScalar { Kind: YamlScalarKind.String } scalar && DurationPattern().IsMatch(scalar.Text) && Parsed(scalar.Text) is { } span && span <= ToolchainTables.MaxTimeout)
        {
            return span;
        }

        Fault($"line {entry.Line}: '{key}' of timeouts must be a whole number and a unit — 90s, 15m or 2h — of at most {ToolchainTables.MaxTimeout.TotalHours:0} hours, not {Shown(entry.Value)}");

        return fallback;
    }

    private static TimeSpan? Parsed(string text)
    {
        var digits = text[..^1];
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return null;
        }

        // The count is compared with the maximum in ITS unit before any TimeSpan is made: a count of 2147483647 hours passes an integer limit and overflows TimeSpan.FromHours, which throws instead of refusing.
        var most = ToolchainTables.MaxTimeout;

        return text[^1] switch
        {
            's' => count <= (long)most.TotalSeconds ? TimeSpan.FromSeconds(count) : null,
            'm' => count <= (long)most.TotalMinutes ? TimeSpan.FromMinutes(count) : null,
            _ => count <= (long)most.TotalHours ? TimeSpan.FromHours(count) : null,
        };
    }

    [GeneratedRegex(ToolchainTables.DurationPattern, RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();
}
