using System.Text.RegularExpressions;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The placeholder pass of a role's prompt: <c>{{name}}</c> is replaced by its value, in ONE pass — a value that holds <c>{{…}}</c> is not read again, so a text a task or an epic carries cannot change the prompt.
/// The BA and the coder both fill their templates here, so the rule is written once.
/// </summary>
internal static partial class PromptTemplate
{
    [GeneratedRegex(@"\{\{([A-Za-z]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    /// <summary>
    /// The template with its placeholders filled. An unknown placeholder in the SHIPPED template is a build fault, so it throws; in a text a person wrote (<paramref name="strict"/> false) it is left as written, because their typo
    /// must not stop a job.
    /// </summary>
    public static string Fill(string role, string template, IReadOnlyDictionary<string, string> values, bool strict = true) =>
        Placeholder().Replace(template, match => values.TryGetValue(match.Groups[1].Value, out var value)
            ? value
            : strict
                ? throw new InvalidOperationException($"the {role} prompt has a placeholder {{{{{match.Groups[1].Value}}}}} that nothing fills — the template and its builder disagree")
                : match.Value);

    /// <summary>An embedded text of a prompt: a missing one is a build fault, so it throws (a default that can go missing is not a default).</summary>
    public static string Embedded(string resource)
    {
        using var stream = typeof(PromptTemplate).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"the embedded {resource} is missing — the build did not embed it");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
