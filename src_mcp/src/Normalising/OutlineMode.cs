using System.Text.Json;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Normalizer;

namespace CoaiMcp.Normalising;

/// <summary>
/// <c>--outline &lt;file&gt; [--json]</c>: one file's body-free outline on stdout, and nothing else.
/// </summary>
/// <remarks>
/// <para><b>Why a mode.</b> The feature review's budget had to be measured before it was built (plan
/// §6, S0.2), and the only honest instrument is the product's own outliner in the product's own AOT
/// binary — a throwaway outliner would have measured a different program. It stays because the same
/// question comes back every time a reviewer's outline looks wrong: "what did the outliner make of
/// this file?"</para>
/// <para><b>Exit codes, per <c>.agents/PROJECT.md</c>.</b> 0 whenever the file was read — including
/// "not outlined", which is an ANSWER printed on stdout (a language it does not read, a file over the
/// ceiling, a parse failure). 65 for a request with no file. 66 when the file cannot be read. Never
/// 64: that code means "this binary has never heard of --outline" and is how a caller detects an old
/// server.</para>
/// <para><b>The ceiling is checked on the file's LENGTH before a byte is read</b>, with the same
/// constant and the same sentence the outliner uses, so pointing this at a bundle costs a stat.</para>
/// </remarks>
internal static class OutlineMode
{
    internal static int Run(string[] args, TextWriter stdout, Action<string> note)
    {
        var file = args.Skip(1).FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? string.Empty;
        if (file.Length == 0)
        {
            note("--outline needs a file: --outline <file> [--json]");

            return 65; // EX_DATAERR — never 64, which would read as "no such mode"
        }

        var outliner = new TreeSitterOutliner();
        var language = outliner.LanguageOf(file);
        SourceOutline outline;
        try
        {
            var length = new FileInfo(file).Length;
            outline = length > OutlineLimits.MaxInputBytes
                ? SourceOutline.TooLarge(language, length)
                : outliner.Outline(language, File.ReadAllText(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            note($"--outline could not read '{file}': {e.Message}");

            return 66; // EX_NOINPUT
        }

        stdout.Write(args.Contains("--json", StringComparer.Ordinal)
            ? JsonSerializer.Serialize(outline, Server.ServerJsonContext.Default.SourceOutline) + "\n"
            : outline.Render());

        return 0;
    }
}
