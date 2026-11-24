using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Core.Notices;

namespace CoaiMcp.Server.Keys;

/// <summary>Everything <see cref="KeysMode"/> touches outside the arguments, so a test runs the whole mode with its own streams, clock and data folder.</summary>
internal sealed record KeysRuntime(
    string DataDir,
    Func<string, string?> Settings,
    IEnumerable<KeyValuePair<string, string>> Variables,
    Stream Stdin,
    bool StdinIsTerminal,
    Func<string?> ReadHidden,
    Action<string> Prompt,
    TextWriter Output,
    Action<string> Note,
    TimeProvider Clock,
    Action<ServerNotice> Notice,
    KeyFolderSeams Seams);

/// <summary>
/// <c>coai-mcp --keys &lt;verb&gt;</c> — the claude key pool of this device, one shot (KEY_POOL_SPEC.md sections 4 and 8): <c>list</c>, <c>add --label X</c> (the text is ONE line on stdin, no echo on a terminal),
/// <c>import</c> (many, for a boot script and for the creds import), <c>relabel</c>, <c>disable</c>, <c>enable</c>, <c>remove</c>, <c>scan --path P</c> and <c>sync</c> (the environment and keys-file sources).
/// </summary>
/// <remarks>
/// <para><b>Selected by <c>args[0]</c> before any transport; never exits 64</b> — this binary KNOWS the mode. <b>65</b> wrong arguments or data (a sentence on stderr), <b>74</b> the store cannot be used (a data folder that
/// does not keep file modes, a disk fault, a named data folder that is missing), <b>75</b> the pool is busy. Stdout carries the answer only; a person's terminal.</para>
/// <para><b>No verb takes a key text as an argument, from a named file or from the environment</b>: there is no such flag, so a text on a command line is an unknown argument — and a sentence never repeats an argument it
/// does not understand, because the text may be the argument. The creds code-access key is never read: <c>creds config | coai-mcp --keys import --format creds-json</c> pipes the entry's body.</para>
/// <para><b>Answers are the Keys page's answers</b>: <c>list</c> is <c>{adminRevision, usable, personSignInSwitch, rows}</c>, and a write that carries <c>--expect-revision</c> is refused as "changed elsewhere" when the list moved since.</para>
/// </remarks>
internal static class KeysMode
{
    internal const string Usage = "--keys takes a verb: list, add, import, relabel, disable, enable, remove, scan or sync — run --help for the flags";

    /// <summary>The switch that lets a role job run on the person's own sign-in while the pool holds no key at all (story t5): shown by <c>list</c>.</summary>
    internal const string PersonSignInSetting = "COAI_ROLE_CLAUDE_PERSON_SIGN_IN";

    private static readonly IReadOnlyDictionary<string, string[]> Flags = new Dictionary<string, string[]>
    {
        ["list"] = ["--json"],
        ["add"] = ["--label", "--account", "--json"],
        ["import"] = ["--format", "--source", "--json"],
        ["relabel"] = ["--id", "--label", "--expect-revision", "--json"],
        ["disable"] = ["--id", "--expect-revision", "--json"],
        ["enable"] = ["--id", "--expect-revision", "--json"],
        ["remove"] = ["--id", "--expect-revision", "--json"],
        ["scan"] = ["--path", "--json"],
        ["sync"] = ["--json"],
    };

    /// <summary>Runs one verb. <paramref name="args"/> are the arguments AFTER <c>--keys</c>.</summary>
    internal static int Run(string[] args, KeysRuntime runtime)
    {
        if (Parse(args) is not { } parsed)
        {
            runtime.Note(args.Length > 0 && Flags.ContainsKey(args[0]) ? $"--keys {args[0]}: an argument is not understood (arguments are never repeated here)" : Usage);

            return KeyExit.Data;
        }

        var access = new KeyStoreAccess(runtime.DataDir, runtime.Clock, runtime.Notice, runtime.Seams);
        var run = new KeyVerbs(runtime, access, parsed);

        return parsed.Verb switch
        {
            "list" => run.List(),
            "add" => run.Add(),
            "import" => run.Import(),
            "relabel" or "disable" or "enable" or "remove" => run.Change(),
            "scan" => run.Scan(),
            _ => run.Sync(),
        };
    }

    /// <summary>The verb and its flags, or null for any other shape: an unknown verb, an unknown or repeated flag, a flag with no value, a stray word.</summary>
    internal static KeyArgs? Parse(string[] args)
    {
        if (args.Length == 0 || !Flags.TryGetValue(args[0], out var allowed))
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var name = args[index];
            if (!allowed.Contains(name) || values.ContainsKey(name))
            {
                return null;
            }

            if (name == "--json")
            {
                values[name] = string.Empty;
            }
            else if (index + 1 < args.Length)
            {
                values[name] = args[++index];
            }
            else
            {
                return null;
            }
        }

        return new KeyArgs(args[0], values);
    }

    /// <summary>Serialises an answer as ONE line.</summary>
    internal static string Line<T>(T answer, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) => JsonSerializer.Serialize(answer, info);
}

/// <summary>The verb and its flag values (a value-less flag is the empty text).</summary>
internal sealed record KeyArgs(string Verb, IReadOnlyDictionary<string, string> Values)
{
    public bool Has(string flag) => Values.ContainsKey(flag);

    public string Get(string flag) => Values.GetValueOrDefault(flag, string.Empty);

    public bool Json => Has("--json");
}

