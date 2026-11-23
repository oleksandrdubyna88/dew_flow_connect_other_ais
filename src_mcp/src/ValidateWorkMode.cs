using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Work;
using CoaiMcp.Roles;
using CoaiMcp.Server;
using CoaiMcp.Work;

namespace CoaiMcp;

/// <summary>
/// <c>coai-mcp --validate-work &lt;feature-dir&gt;</c> and the MCP tool <c>validate_work(repoPath, featureFolder)</c> — a work plan (the YAML folder tree of <c>shared/work-method.md</c>)
/// checked against the work-plan contract, answered as ONE JSON object (epoch 3 E1 s2). Both surfaces run <see cref="Evaluate"/>, so for the same folder they answer the same bytes.
/// </summary>
/// <remarks>
/// <para><b>A reader, and it never makes the data directory.</b> It reads the WORKING TREE and the device's settings (the environment over the data directory's settings file, through
/// <see cref="RolesMode.Layered"/>, which writes nothing) and opens no database. <c>Program.WriterModes</c> does not hold it, and
/// <c>AReaderModeNeverMakesTheDataDirectoryTests</c> runs it against a missing named root.</para>
/// <para><b>The limits are the device's.</b> Epics per epoch, MVP epics, stories per epic and tasks per story are the work method of the <c>roles</c> answer — the shipped defaults with a person's
/// <c>COAI_ROLES_WORK_METHOD</c> over them — and the answer says <c>source: device</c> or <c>default</c> and the <c>settingsRevision</c>, because a plan valid on one device can be invalid on
/// another. A setting that did not read is a note, so the numbers that were used are never a surprise.</para>
/// <para><b>Exit codes.</b> 0 a valid plan; 1 an invalid one (the answer lists the errors); <b>65</b> for the wrong arguments, a path that is not a folder or a folder with no
/// <c>feature.yaml</c> — the binary HAS the mode and refuses the request; <b>74</b> for a read the disk refuses. <b>Never 64</b>: that is "never heard of this mode" and how a caller takes an
/// old binary.</para>
/// </remarks>
internal static class ValidateWorkMode
{
    internal const string Usage = "--validate-work takes one feature folder: coai-mcp --validate-work <feature-dir>";

    /// <summary>What <see cref="Evaluate"/> decided: the exit code, the answer when there is one, and the sentence when the request was refused.</summary>
    internal readonly record struct Outcome(int Code, string Json, string Refusal);

    internal static int Run(string[] args, Func<string, string?> env, string workingDirectory, TextWriter output, Action<string> note)
    {
        var rest = args.Skip(1).ToList();
        if (rest.Count != 1 || rest[0].StartsWith("--", StringComparison.Ordinal))
        {
            note(Usage);

            return 65; // EX_DATAERR — this binary HAS the mode and is refusing the request
        }

        var shown = Spelled(rest[0]);
        var outcome = Evaluate(env, Path.GetFullPath(shown, workingDirectory), shown);
        if (outcome.Refusal.Length > 0)
        {
            note($"--validate-work: {outcome.Refusal}");

            return outcome.Code;
        }

        output.WriteLine(outcome.Json);

        return outcome.Code;
    }

    /// <summary>The folder as the answer names it: one spelling, <c>/</c> for <c>\</c>, no trailing separator.</summary>
    internal static string Spelled(string folder) => folder.Trim().Replace('\\', '/').TrimEnd('/') is { Length: > 0 } text ? text : "/";

    /// <summary>Validates the plan in <paramref name="folder"/> (an absolute path) and builds the answer. Never throws for a plan that is wrong or a folder that is missing.</summary>
    internal static Outcome Evaluate(Func<string, string?> env, string folder, string shown)
    {
        if (!Directory.Exists(folder))
        {
            return new Outcome(65, string.Empty, $"{shown} is not a folder");
        }

        var layered = RolesMode.Layered(env);
        var roles = RolesAnswer.Compute(layered, string.Empty, string.Empty, claudeHook: false);
        try
        {
            var report = new WorkPlanValidator(new StrictYamlReader()).Validate(new DiskWorkFolder(folder), roles.WorkMethod.ToLimits());
            if (!report.FeatureFound)
            {
                return new Outcome(65, string.Empty, $"{shown} has no feature.yaml, so it is not a work plan");
            }

            return new Outcome(report.Valid ? 0 : 1, Json(report, roles, shown, SourceOf(layered)), string.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Outcome(74, string.Empty, $"the plan could not be read: {e.Message}");
        }
    }

    /// <summary><c>device</c> when a person set the work method, <c>default</c> when the shipped numbers stand.</summary>
    private static string SourceOf(Func<string, string?> layered) => layered(RolesKeys.WorkMethod) is { Length: > 0 } ? "device" : "default";

    private static string Json(WorkReport report, RolesResult roles, string shown, string source)
    {
        var method = roles.WorkMethod;
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, RolesJson.Options))
        {
            json.WriteStartObject();
            json.WriteString("mode", "validate-work");
            json.WriteString("featureFolder", shown);
            json.WriteBoolean("valid", report.Valid);
            json.WriteStartObject("counts");
            json.WriteNumber("epochs", report.Counts.Epochs);
            json.WriteNumber("epics", report.Counts.Epics);
            json.WriteNumber("stories", report.Counts.Stories);
            json.WriteNumber("tasks", report.Counts.Tasks);
            json.WriteEndObject();
            json.WriteStartObject("limits");
            json.WriteNumber("maxEpicsPerEpoch", method.MaxEpicsPerEpoch);
            json.WriteStartObject("mvpEpics");
            json.WriteNumber("min", method.MvpMin);
            json.WriteNumber("max", method.MvpMax);
            json.WriteEndObject();
            json.WriteNumber("maxStoriesPerEpic", method.MaxStoriesPerEpic);
            json.WriteNumber("maxTasksPerStory", method.MaxTasksPerStory);
            json.WriteString("source", source);
            json.WriteString("settingsRevision", roles.SettingsRevision);
            json.WriteEndObject();
            WriteErrors(json, report);
            WriteNotes(json, report, roles);
            json.WriteStartArray("important");
            foreach (var item in report.Important)
            {
                json.WriteStartObject();
                json.WriteString("node", item.Node);
                json.WriteString("reason", item.Reason);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteBoolean("truncated", report.Truncated);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteErrors(Utf8JsonWriter json, WorkReport report)
    {
        json.WriteStartArray("errors");
        foreach (var error in report.Errors)
        {
            json.WriteStartObject();
            json.WriteString("code", error.Code);
            json.WriteString("path", error.Path);
            json.WriteString("node", error.Node);
            json.WriteString("message", error.Message);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>The plan's notes, then a note for each work-method setting this device could not read (its default stood, so the limits above are not what the person wrote).</summary>
    private static void WriteNotes(Utf8JsonWriter json, WorkReport report, RolesResult roles)
    {
        json.WriteStartArray("notes");
        foreach (var note in report.Notes.Concat(roles.Unrecognised.Where(problem => problem.Key == RolesKeys.WorkMethod).Select(problem => problem.Sentence)))
        {
            json.WriteStringValue(note);
        }

        json.WriteEndArray();
    }
}
