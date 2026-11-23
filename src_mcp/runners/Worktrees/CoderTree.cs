using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>What <see cref="CoderTree.Open"/> answers: the tree, or the sentence that says why it is not one coai can trust.</summary>
public abstract record CoderTreeOpening
{
    /// <summary>The tree, with its two paths resolved and its <c>.git</c> pointer written down.</summary>
    public sealed record Opened(CoderTree Tree) : CoderTreeOpening;

    /// <summary>The folder is not a linked worktree of that checkout; nothing was started.</summary>
    public sealed record Refused(string Sentence) : CoderTreeOpening;

    private CoderTreeOpening() { }
}

/// <summary>
/// A coder worktree as coai holds it while a job runs: where it is, the main checkout it was made from, and the text of its <c>.git</c> pointer as it was when coai looked — so a later change of that file is a fact coai can see
/// before it lets git read through it (epoch 3 E2 s2 t4 d5; <c>v2/spikes/RESULTS_coder_write_boundary_judgement.md</c> must-change 3).
/// </summary>
/// <remarks>
/// <para><b>Why the pointer matters.</b> A linked worktree's <c>.git</c> is a FILE: <c>gitdir: &lt;main&gt;/.git/worktrees/&lt;name&gt;</c>. Git trusts it. A check or a coder that replaced it could point coai's next git command at any repository —
/// or at a folder holding hooks and config of its own. Two measures: during the checks the file is bound read-only (<see cref="Toolchain.CheckBoundary"/>), and before each coai git command in the tree the file is compared with what was
/// written down here (<see cref="Verify"/>).</para>
/// <para>Both paths are real paths (links resolved): a bind mount and a comparison need one spelling.</para>
/// </remarks>
/// <param name="Root">The worktree's root folder. Every coai git command on the tree runs with this as its working folder, never a folder below it.</param>
/// <param name="MainCheckout">The main repository's work folder. The base toolchain is read from here.</param>
/// <param name="ExpectedPointer">The text of <c>Root/.git</c> when the tree was opened.</param>
public sealed record CoderTree(string Root, string MainCheckout, string ExpectedPointer)
{
    private const string Prefix = "gitdir: ";

    /// <summary>The <c>.git</c> entry of a tree.</summary>
    public string GitEntry => Path.Combine(Root, ".git");

    /// <summary>Looks at <paramref name="root"/> and writes its pointer down, or refuses with a sentence. Reads files only; no process.</summary>
    public static CoderTreeOpening Open(string root, string mainCheckout)
    {
        var tree = RealPath.Resolve(root);
        var main = RealPath.Resolve(mainCheckout);
        if (!Path.IsPathFullyQualified(root) || !Path.IsPathFullyQualified(mainCheckout) || !tree.Ok || !main.Ok)
        {
            return Refuse($"The worktree '{root}' and the main checkout '{mainCheckout}' must be absolute paths that can be resolved.");
        }

        var pointer = ReadPointer(Path.Combine(tree.Path, ".git"));
        if (pointer.Sentence.Length > 0)
        {
            return Refuse(pointer.Sentence);
        }

        var worktrees = Path.Combine(main.Path, ".git", "worktrees");

        return RealPath.IsInside(worktrees, pointer.GitDir, StringComparison.Ordinal) && !string.Equals(Path.TrimEndingDirectorySeparator(worktrees), Path.TrimEndingDirectorySeparator(pointer.GitDir), StringComparison.Ordinal)
            ? new CoderTreeOpening.Opened(new CoderTree(tree.Path, main.Path, pointer.Text))
            : Refuse($"The .git file of {tree.Path} points at '{pointer.GitDir}', which is not a worktree folder of {main.Path}; it was not trusted.");
    }

    /// <summary>
    /// Whether the <c>.git</c> pointer is still the file coai wrote down: a regular file (never a link or a folder) with the same text. Empty answer = intact; else the sentence.
    /// Reads the file; the answer is for THIS moment, so it is asked again before every git command.
    /// </summary>
    public string Verify()
    {
        var pointer = ReadPointer(GitEntry);

        return pointer.Sentence.Length > 0
            ? $"The .git pointer of the coder worktree changed: {pointer.Sentence}"
            : string.Equals(pointer.Text, ExpectedPointer, StringComparison.Ordinal)
                ? string.Empty
                : $"The .git pointer of the coder worktree {Root} changed since coai wrote it down; no git command was run in the tree.";
    }

    private sealed record Pointer(string Text, string GitDir, string Sentence);

    private static Pointer ReadPointer(string entry)
    {
        var kind = RealPath.Lstat(entry).Kind;
        if (kind != EntryKind.File)
        {
            return new Pointer(string.Empty, string.Empty, $"{entry} is {(kind == EntryKind.Missing ? "not there" : "not a regular file")}; a linked worktree has a .git FILE.");
        }

        string text;
        try
        {
            text = File.ReadAllText(entry);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Pointer(string.Empty, string.Empty, $"{entry} could not be read ({e.Message}).");
        }

        var line = text.TrimEnd('\r', '\n');

        return line.StartsWith(Prefix, StringComparison.Ordinal) && !line.Contains('\n', StringComparison.Ordinal)
            ? new Pointer(text, line[Prefix.Length..].Trim(), string.Empty)
            : new Pointer(text, string.Empty, $"{entry} does not hold one line 'gitdir: <path>'.");
    }

    private static CoderTreeOpening.Refused Refuse(string sentence) => new(sentence);
}
