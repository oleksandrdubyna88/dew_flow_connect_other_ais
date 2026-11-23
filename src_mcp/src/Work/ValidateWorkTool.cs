using System.Text.Json;
using CoaiMcp.Server;

namespace CoaiMcp.Work;

/// <summary>
/// The arguments of the <c>validate_work</c> tool, checked before anything is read: the repository is a folder, the feature folder is a relative path inside it (the rule of
/// <c>workItem</c>, <see cref="WorkItems.Check"/>), and no folder on the way is a link, because a link in the repository must not lead the read out of it. The answer of a good call IS
/// the one-shot's (<see cref="ValidateWorkMode.Evaluate"/>); a refused call answers <c>{"error": "..."}</c>, the shape every refusal here has.
/// </summary>
internal static class ValidateWorkTool
{
    internal static string Answer(Func<string, string?> env, string repoPath, string featureFolder)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return Error($"repoPath '{repoPath}' is not a folder");
        }

        var (item, refusal) = WorkItems.Check(featureFolder);
        if (refusal.Length > 0 || item.Length == 0)
        {
            return Error(item.Length == 0 && refusal.Length == 0
                ? "featureFolder is empty — send the repository-relative path of the folder that holds feature.yaml, such as v2"
                : $"featureFolder{refusal[(refusal.IndexOf(' ') < 0 ? 0 : refusal.IndexOf(' '))..refusal.IndexOf(" — ", StringComparison.Ordinal)]} — send the folder as it is written from the repository's top level, such as v2");
        }

        var root = Path.GetFullPath(repoPath);
        if (LinkOnTheWay(root, item) is { Length: > 0 } link)
        {
            return Error($"featureFolder runs through the link {link}, and a link is not followed");
        }

        var outcome = ValidateWorkMode.Evaluate(env, Path.Combine(root, item.Replace('/', Path.DirectorySeparatorChar)), item);

        return outcome.Refusal.Length > 0 ? Error(outcome.Refusal) : outcome.Json;
    }

    /// <summary>The first folder on the way from the repository down to the feature folder that is a link, or empty.</summary>
    private static string LinkOnTheWay(string root, string item)
    {
        var at = root;
        var walked = new List<string>();
        foreach (var segment in item.Split('/'))
        {
            at = Path.Combine(at, segment);
            walked.Add(segment);
            if (Directory.Exists(at) && new DirectoryInfo(at).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return string.Join('/', walked);
            }
        }

        return string.Empty;
    }

    private static string Error(string sentence)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Roles.RolesJson.Options))
        {
            json.WriteStartObject();
            json.WriteString("error", sentence);
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
