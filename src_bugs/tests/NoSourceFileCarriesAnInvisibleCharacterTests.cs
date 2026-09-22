using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// No source file in this repository contains a bidirectional control character.
/// </summary>
/// <remarks>
/// <para><b>Written because the rule that refuses these was itself written in them.</b>
/// <c>CommentRule.Reordering</c> listed the twelve controls as the characters THEMSELVES, so every
/// literal rendered as an empty pair of quotes. It compiled, it worked, and it was indefensible: a
/// reviewer could not see what it matched (three in one round read it as broken), and a formatter,
/// an editor or a copy through any tool that normalises or strips them would have silently emptied
/// part of the whitelist while every behavioural test kept passing on the ranges that survived.</para>
/// <para><b>And it survived one attempt to fix it.</b> The escapes were written, the build was green,
/// the tests were green, and the file still held the raw characters — because a code point
/// written as a backslash-u escape on the way to a file does not always arrive as six ASCII
/// bytes. Nothing in the suite could tell the two apart, so the claim "this is fixed" was made twice about a file that had
/// not changed. SonarCloud's S6389 caught it. This test is that catch, moved to where it costs
/// seconds instead of a review round.</para>
/// <para><b>It reads BYTES, not text.</b> Decoding first and asking about characters is the same
/// mistake one level up: the point is what is on disk. A code point is named in prose or built from
/// its number; it is never spelled out in a literal here or anywhere this scans.</para>
/// <para>This is the "Trojan Source" defence for the whole checkout, not only for the comment rule —
/// the attack is a source file that renders to a reviewer as something other than what it compiles
/// to, and it does not care which file it lives in.</para>
/// </remarks>
public sealed class NoSourceFileCarriesAnInvisibleCharacterTests
{
    /// <summary>
    /// The twelve controls that can reorder rendered text, as UTF-8 byte sequences.
    /// </summary>
    /// <remarks>
    /// Built from their NUMBERS, so this file cannot contain what it is looking for. The set is the
    /// one <c>CommentRule</c> refuses in a contributor's comment: U+061C, U+200E, U+200F,
    /// U+202A–U+202E and U+2066–U+2069.
    /// </remarks>
    private static List<(string Named, byte[] Utf8)> Invisible =>
        [.. Reordering().Select(code => ($"U+{code:X4}", Encoding.UTF8.GetBytes(char.ConvertFromUtf32(code))))];

    private static IEnumerable<int> Reordering() =>
        [0x061c, 0x200e, 0x200f, .. Enumerable.Range(0x202a, 5), .. Enumerable.Range(0x2066, 4)];

    /// <summary>Every C# and TypeScript file this repository owns.</summary>
    /// <remarks>
    /// Walked from the repository root rather than from a list, so a new project is covered the day
    /// it exists. Build output and dependencies are skipped: they are not this repository's text, and
    /// a vendored package that legitimately carries one of these would turn a real guard into noise.
    /// </remarks>
    private static IEnumerable<string> Sources()
    {
        var root = Repository();
        // `.agents` is the conventions SUBMODULE: its text belongs to another repository, which
        // this one cannot fix, and Sonar excludes it here for the same reason.
        string[] skip =
            ["bin", "obj", "node_modules", "out", ".git", ".vscode-test", "artifacts", ".agents"];

        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal)
                || path.EndsWith(".ts", StringComparison.Ordinal)
                || path.EndsWith(".mjs", StringComparison.Ordinal))
            .Where(path => !Path.GetRelativePath(root, path)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => skip.Contains(part, StringComparer.Ordinal)));
    }

    [Fact]
    public void NoCSharpOrTypeScriptFileCarriesABidirectionalControl()
    {
        var invisible = Invisible;

        var carrying = Sources()
            .SelectMany(path => Found(path, invisible))
            .ToList();

        carrying.Should().BeEmpty(
            "a source file that renders to a reviewer as something other than what it compiles to is "
            + "the Trojan Source shape; name the code point in prose, or build it from its number");
    }

    /// <summary>What one file carries, named by file and code point.</summary>
    private static IEnumerable<string> Found(
        string path, List<(string Named, byte[] Utf8)> invisible)
    {
        var bytes = File.ReadAllBytes(path);

        return invisible
            .Where(one => Contains(bytes, one.Utf8))
            .Select(one => $"{path} carries {one.Named}");
    }

    /// <summary>Whether this byte sequence occurs, without decoding the file first.</summary>
    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    /// <summary>
    /// The repository root, found by walking up to the folder that has a `.git`.
    /// </summary>
    /// <remarks>
    /// A fixed number of `..` from <see cref="AppContext.BaseDirectory"/> is what breaks the moment a
    /// build lands anywhere but `bin/Debug/net10.0`; the marker is what the repository IS.
    /// </remarks>
    private static string Repository()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, ".git"))
            && !File.Exists(Path.Combine(here.FullName, ".git")))
        {
            here = here.Parent;
        }

        here.Should().NotBeNull("this suite runs from inside the checkout it is scanning");

        return here!.FullName;
    }
}
