using CoaiMcp.Server;

namespace CoaiMcp.Roles;

/// <summary>
/// <c>coai-mcp --roles [--format json|claude-hook]</c> — the roles answer as ONE object on stdout, then exit 0 (v2 E3.S7 t1). <c>json</c> is the <c>roles</c> tool's answer; <c>claude-hook</c> is Claude
/// Code's SessionStart hook output (<c>hookSpecificOutput.additionalContext</c> holds a short role table and the work-method sentence), which the installer registers.
/// </summary>
/// <remarks>
/// <para><b>A reader, and it never makes the data directory.</b> It reads the settings file of the data directory when there is one (under the environment, which wins) and writes nothing —
/// not even the adoption of a root settings file that <see cref="SettingsFile.Layer(string, Func{string, string?}, Action{string})"/> does. <c>Program.WriterModes</c> does not hold it.</para>
/// <para><b>Exit codes.</b> 0 with the object; <b>65</b> for an unknown format or argument — never 64, which is "never heard of this mode" and how a hook's caller takes an old binary.
/// A hook must never fail a session, so an error here is the caller's to ignore: the sentence goes to stderr, nothing to stdout.</para>
/// </remarks>
internal static class RolesMode
{
    internal const string Usage = "--roles takes only [--format json|claude-hook]";

    internal static int Run(string[] args, Func<string, string?> env, TextWriter output, Action<string> note)
    {
        var format = "json";
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--format" && index + 1 < args.Length && args[index + 1] is "json" or "claude-hook")
            {
                format = args[++index];
            }
            else
            {
                note(Usage);

                return 65; // EX_DATAERR — this binary HAS the mode and is refusing the request
            }
        }

        var result = RolesAnswer.Compute(Layered(env), string.Empty, string.Empty, claudeHook: format == "claude-hook");
        foreach (var problem in result.Unrecognised)
        {
            note(problem.Sentence);
        }

        output.WriteLine(format == "claude-hook" ? RolesAnswer.HookJson(result) : RolesAnswer.Json(result));

        return 0;
    }

    /// <summary>The environment over the data directory's settings file, read and never written; a missing or unreadable file is no settings.</summary>
    internal static Func<string, string?> Layered(Func<string, string?> env)
    {
        var file = SettingsFile.Read(Path.Combine(SettingsFile.DataDirFrom(env).Path, SettingsFile.Name));

        return name => env(name) is { Length: > 0 } fromEnv ? fromEnv : file.GetValueOrDefault(name);
    }
}
