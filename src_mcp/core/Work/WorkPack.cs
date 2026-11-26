using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Feature;

namespace CoaiMcp.Core.Work;

/// <summary>One line of the approval: <c>F &lt;blob&gt; &lt;path&gt;</c> for a packed file, <c>D &lt;node&gt; &lt;title&gt;</c> for a dependency outside the scope.</summary>
/// <param name="Kind"><c>F</c> or <c>D</c>.</param>
/// <param name="Key">The repository-relative path of an <c>F</c> line, the node of a <c>D</c> line.</param>
/// <param name="Value">The blob id of an <c>F</c> line, the title of a <c>D</c> line.</param>
public sealed record ApprovalLine(string Kind, string Key, string Value)
{
    /// <summary>The line as it is hashed, without its line end.</summary>
    public string Text => Kind == "F" ? $"F {Value} {Key}" : $"D {Key} {Value}";
}

/// <summary>One file section of the pack: the repository-relative path and the size of the file in it.</summary>
public sealed record PackSection(string Path, long Bytes);

/// <summary>What <see cref="WorkPack.Build"/> made.</summary>
/// <param name="Text">What the reviewers read under the plan heading: the line Not sent, then one fenced section per file. The line that says who packed it is <see cref="WorkPack.SourceLine"/>.</param>
/// <param name="Scope">The repository-relative folder of the scope (the feature folder when the whole feature is the scope).</param>
/// <param name="ApprovalId">SHA-256, in hex, over <paramref name="Approval"/> — one line each, ended by a line feed.</param>
/// <param name="Approval">The <c>F</c> lines in pack order, then the <c>D</c> lines in pack order.</param>
/// <param name="Sections">The files in pack order.</param>
/// <param name="Bytes">The UTF-8 size of <paramref name="Text"/>.</param>
public sealed record Pack(string Text, string Scope, string ApprovalId, IReadOnlyList<ApprovalLine> Approval, IReadOnlyList<PackSection> Sections, int Bytes);

/// <summary>A pack, or the one sentence that says why there is none.</summary>
public abstract record PackOutcome
{
    private PackOutcome()
    {
    }

    public sealed record Packed(Pack Pack) : PackOutcome;

    public sealed record Refused(string Sentence) : PackOutcome;
}

/// <summary>
/// The work pack of a plan round over a feature folder (epoch 3 E3 s3; <c>SPEC.md</c> section B): the text the reviewers read for a SCOPE, in topological order, and the lines of the approval. Pure: the folder it reads is handed in, and no
/// disk, clock or environment is touched.
/// </summary>
/// <remarks>
/// <para><b>Order</b> (<see cref="WorkPackOrder"/>): <c>feature.yaml</c>; the feature's <c>plan</c> file; the context of the scope — each epoch's file, then the epic files and the epic's <c>spec</c> file; then the stories, each after the
/// stories it depends on, and in a story each task after the tasks it depends on. Ties go by the number in the id (<c>s2</c> before <c>s10</c>). One line (node and title) for each node outside the scope a scope node depends on.</para>
/// <para><b>Limits.</b> A file of the pack is at most <see cref="FileBytes"/> bytes, the folder at most <see cref="Entries"/> entries and the pack <see cref="PackBytes"/> bytes. Over any limit is a refusal that says the size; nothing is cut.</para>
/// <para><b>The approval</b> is what the reviewers read: the id covers every packed file by its blob id and every outside dependency by its node and title. A file the pack does not hold (a <c>RESULTS_*.md</c>, a node nobody in the scope depends
/// on) is not in it.</para>
/// </remarks>
public static class WorkPack
{
    /// <summary>The largest pack, in UTF-8 bytes — the ceiling a committed plan has.</summary>
    public const int PackBytes = 256 * 1024;

    /// <summary>The largest file of the pack.</summary>
    public const int FileBytes = 64 * 1024;

    /// <summary>The most entries a folder may hold (<c>GitWorkFolder</c> refuses more before it reads a blob).</summary>
    public const int Entries = 2000;

    /// <summary>
    /// Packs <paramref name="scopeFolder"/> of the plan in <paramref name="folder"/>. <paramref name="featureFolder"/> and <paramref name="scopeFolder"/> are repository-relative; the folder's own paths are relative to the feature folder.
    /// The plan is expected to have been validated: a folder that is not a clean plan is refused here too, never half packed.
    /// </summary>
    public static PackOutcome Build(IListedWorkFolder folder, IYamlReader reader, string featureFolder, string scopeFolder)
    {
        var errors = new List<WorkError>();
        var feature = folder.Files.Count > Entries ? null : new WorkPlanLoader(folder, reader, errors).Load();
        if (feature is null || errors.Count > 0)
        {
            return Refuse(NotAPlan(folder, errors, featureFolder));
        }

        var edges = WorkDependencyRules.Check(feature, errors);
        var scope = errors.Count > 0 ? null : WorkPackScope.Of(feature, featureFolder, scopeFolder);

        return scope is null ? Refuse(errors.Count > 0 ? NotAPlan(folder, errors, featureFolder) : $"'{scopeFolder}' names no node of the plan in '{featureFolder}' — name an epoch, an epic, a story or a task folder, as it is written from the repository's top level")
            : Packed(folder, feature, edges, scope, featureFolder);
    }

    /// <summary>The files, besides YAML, the pack reads by the names the plan gives them: the <c>plan</c> file and each epic's <c>spec</c> file, as paths from the feature folder. A name that is not a path inside the folder is left out here and refused by <see cref="Build"/>.</summary>
    public static IReadOnlyList<string> NamedFiles(IWorkFolder folder, IYamlReader reader)
    {
        var feature = new WorkPlanLoader(folder, reader, []).Load();

        return feature is null ? [] : [.. WorkPackFiles.Named(feature).Where(name => name.Refusal.Length == 0).Select(name => name.Path)];
    }

    /// <summary>The line the prompt carries under its heading: who packed what, at which commit, under which approval.</summary>
    public static string SourceLine(Pack pack, string commit, string limitsSource) =>
        $"(packed by the server from {pack.Scope} at commit {GitIds.Short(commit)}, approval {pack.ApprovalId}, {pack.Sections.Count.ToString(CultureInfo.InvariantCulture)} files, {pack.Bytes.ToString(CultureInfo.InvariantCulture)} bytes; validate_work: valid, limits {limitsSource})";

    private static PackOutcome Packed(IListedWorkFolder folder, WorkFeature feature, IReadOnlyDictionary<string, IReadOnlyList<string>> edges, WorkPackScope scope, string featureFolder)
    {
        var plan = WorkPackOrder.Of(feature, edges, scope);
        var read = WorkPackFiles.Read(folder, plan.Files, featureFolder);
        if (read.Refusal.Length > 0)
        {
            return Refuse(read.Refusal);
        }

        var approval = Approval(read.Sections, plan.Dependencies);
        // A story or a task is read WITHOUT the epics and stories it depends on, so the reviewers are told which they are; the text of an epoch or an epic scope stays what it was, byte for byte (an approved round of one carries its text id).
        var text = WorkPackText.Compose(read.Sections, WorkPackText.NotSent(folder, feature, plan, featureFolder), scope.Story is not null ? plan.Dependencies : []);
        var bytes = Encoding.UTF8.GetByteCount(text);
        var pack = new Pack(text, scope.Folder.Length == 0 ? featureFolder : $"{featureFolder}/{scope.Folder}", IdOf(approval), approval, [.. read.Sections.Select(section => new PackSection(section.Path, section.Bytes))], bytes);

        return bytes > PackBytes ? Refuse(TooLarge(pack)) : new PackOutcome.Packed(pack);
    }

    private static List<ApprovalLine> Approval(IReadOnlyList<WorkPackFiles.Section> sections, IReadOnlyList<WorkPackOrder.Dependency> dependencies) =>
        [.. sections.Select(section => new ApprovalLine("F", section.Path, section.Blob)), .. dependencies.Select(dependency => new ApprovalLine("D", dependency.Node, dependency.Title))];

    private static string IdOf(IEnumerable<ApprovalLine> approval) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(approval.Select(line => line.Text + "\n")))));

    private static string TooLarge(Pack pack)
    {
        var largest = pack.Sections.OrderByDescending(section => section.Bytes).ThenBy(section => section.Path, StringComparer.Ordinal).Take(3)
            .Select(section => $"{section.Path} ({section.Bytes.ToString(CultureInfo.InvariantCulture)} bytes)");

        return $"the pack of '{pack.Scope}' is {pack.Bytes.ToString(CultureInfo.InvariantCulture)} bytes, over the {PackBytes.ToString(CultureInfo.InvariantCulture)} the reviewers may be sent — nothing was cut. "
            + $"The three largest sections: {string.Join(", ", largest)}. Review a smaller scope: pass workItem naming an epic or a story of the plan, or make the files smaller.";
    }

    private static string NotAPlan(IListedWorkFolder folder, List<WorkError> errors, string featureFolder) =>
        folder.Files.Count > Entries
            ? $"'{featureFolder}' holds {folder.Files.Count.ToString(CultureInfo.InvariantCulture)} files, over the {Entries.ToString(CultureInfo.InvariantCulture)} a plan folder may hold — nothing was packed"
            : errors.Count > 0
                ? $"'{featureFolder}' is not a valid plan ({errors[0].Code} at {errors[0].Path}: {errors[0].Message}) — run validate_work and fix it before a pack is made"
                : $"'{featureFolder}' has no feature.yaml, so it is not a work plan";

    private static PackOutcome Refuse(string sentence) => new PackOutcome.Refused(sentence);

    /// <summary>Whether a file name given by the plan (<c>plan</c>, <c>spec</c>) can be read as a file of the folder; the sentence says what is wrong, or is empty.</summary>
    internal static string NameRefusal(string key, string name, string where, string path)
    {
        if (name.Trim().Length == 0 || name.Contains('\\') || name.EndsWith('/'))
        {
            return $"{where}: {key} '{name}' is not a file name — write a path with '/' from {(key == "plan" ? "the feature folder" : "the epic folder")}, such as {(key == "plan" ? "PLAN.md" : "SPEC.md")}";
        }

        if (RepoPaths.WhyNotRelative(path) is { Length: > 0 } why)
        {
            return $"{where}: {key} '{name}' {why} — it must name a file inside the feature folder";
        }

        return CredentialFiles.WhichPattern(path) is { Length: > 0 } shape ? $"{where}: {key} {CredentialFiles.NamedSentence(name, shape)}" : string.Empty;
    }
}
