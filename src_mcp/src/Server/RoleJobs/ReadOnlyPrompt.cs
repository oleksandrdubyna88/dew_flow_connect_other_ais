using System.Globalization;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What the scout's prompt and the judge's prompt have in common (epoch 3 E3 s1 t4): the placeholder pass with a person's override first, the brief, and the sentence about the file the brief is about. The two roles bring their
/// own numbers — read from the tables of their readers, never typed into a prompt file — and their own schema text.
/// </summary>
/// <remarks>
/// <para><b>A person's text first.</b> An override of the prompt <c>prompts/&lt;id&gt;.md</c> in <see cref="RolePrompts"/> is used when it has text, with the same placeholders filled; an empty one is no override. Its unknown placeholders
/// stay as written — a person's typo does not stop a job — while one in the shipped template is a build fault and throws (<see cref="PromptTemplate"/>).</para>
/// <para>One pass over the template, so a brief that holds <c>{{…}}</c> cannot change the prompt.</para>
/// </remarks>
internal static class ReadOnlyPrompt
{
    /// <summary>The prompt of <paramref name="role"/>: the person's override when it has text, else the shipped text, with <paramref name="values"/> placed.</summary>
    public static string Build(string role, string promptId, string shippedResource, RoleJobRequest request, RolePrompts prompts, Dictionary<string, string> values)
    {
        var written = prompts.Written(promptId);
        var custom = !string.IsNullOrWhiteSpace(written);
        values["brief"] = request.Brief;
        values["taskFileBlock"] = TaskFileBlock(request.TaskFile);

        return PromptTemplate.Fill(role, custom ? written : PromptTemplate.Embedded(shippedResource), values, strict: !custom);
    }

    /// <summary>What the prompt says of the file the brief is about: its path as the model reads it, or that none was named.</summary>
    public static string TaskFileBlock(string taskFile) => taskFile.Length == 0
        ? "No file is named: find what the question needs in the repository."
        : $"The question is about the file `{taskFile}` (a path from the top of the repository). Read it by that path.";

    /// <summary>A number as the prompt writes it.</summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
