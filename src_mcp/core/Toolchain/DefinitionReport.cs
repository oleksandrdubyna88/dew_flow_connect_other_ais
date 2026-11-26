using System.Globalization;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// <b>What the head did to the definitions of the checks, in one flag and one line</b> (owner decision 2026-11-26, which replaced the interim gate: nothing the head changes is refused; the head's own definitions run and the difference is REPORTED). The flag is <see cref="Changed"/>; the line is
/// <see cref="Summary"/>, for a person who reads the envelope and does not read the diff.
/// </summary>
/// <param name="Changed">True when the head changed a definition or configuration file, a symlink or a submodule, lost a step the base had, changed a test command, or changed the toolchain file.</param>
/// <param name="Summary">One line; empty when nothing of that happened and no test file was modified or deleted.</param>
public sealed record DefinitionReport(bool Changed, string Summary)
{
    public static DefinitionReport None { get; } = new(false, string.Empty);

    /// <summary>The report of one round.</summary>
    /// <param name="changes">Lost steps and changed definitions from the resolving (null when the round never resolved).</param>
    /// <param name="files">The definition and configuration files, symlinks and submodules the head changed (null when the head was not read).</param>
    /// <param name="file">What the head did to the repository's toolchain file.</param>
    /// <param name="tests">How many test files the head modified, added and deleted.</param>
    public static DefinitionReport Of(DefinitionChanges? changes, ChangedFiles? files, ToolchainChange file, TestFileCounts? tests)
    {
        var parts = new List<string>();
        var changed = false;
        if (files is { Total: > 0 })
        {
            changed = true;
            parts.Add($"{Number(files.Definitions)} definition or configuration file(s) and {Number(files.Links)} symlink or submodule(s) changed");
        }

        if (changes is { Any: true })
        {
            changed = true;
            parts.Add($"{Number(changes.LostSteps.Count)} step(s) the base had are lost, {Number(changes.ChangedDefinitions.Count)} definition member(s) differ, {Number(changes.ChangedTestCommands.Count)} of them about tests");
        }

        if (file != ToolchainChange.Unchanged)
        {
            changed = true;
            parts.Add($"the toolchain file was {file.ToString().ToLowerInvariant()}");
        }

        if (tests is { Modified: > 0 } or { Deleted: > 0 })
        {
            parts.Add($"test files: {Number(tests.Modified)} modified, {Number(tests.Deleted)} deleted");
        }

        return parts.Count == 0
            ? None
            : new DefinitionReport(changed, $"The head changed what the checks are made of: {string.Join("; ", parts)}. A green check means the head's own definitions passed, and the base's tests may have been changed by the coder.");
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
