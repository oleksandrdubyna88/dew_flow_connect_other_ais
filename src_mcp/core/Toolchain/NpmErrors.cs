using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The error block of a failed <c>npm ci</c> (E4 s4 t2; recorded in <c>node-npm-ci-error.txt</c> and <c>node-npm-ci-no-network.txt</c>): the lines <c>npm error code EUSAGE</c> and the first lines of the message after it, up to the usage text or the pointer to npm's own log. One item with npm's code as the
/// <c>code</c>. The marker of this tool is the line <c>npm error code …</c>.
/// </summary>
internal static partial class NpmErrors
{
    private const int MessageLines = 8;

    internal static List<DigestItem> Read(string step, string text)
    {
        var errors = DigestReading.Lines(text).Select(line => DigestReading.Try(Error(), line)).Where(match => match is not null).Select(match => match!.Groups["rest"].Value.Trim()).ToList();
        var code = errors.Select(line => DigestReading.Try(Code(), line)).FirstOrDefault(match => match is not null);
        if (code is null)
        {
            return [];
        }

        var message = errors.SkipWhile(line => DigestReading.Try(Code(), line) is null).Skip(1)
            .TakeWhile(line => !line.StartsWith("Clean install", StringComparison.Ordinal) && !line.StartsWith("Usage:", StringComparison.Ordinal) && !line.StartsWith("A complete log", StringComparison.Ordinal))
            .Where(line => line.Length > 0).Take(MessageLines);

        return [DigestReading.Item(step, string.Empty, 0, code.Groups["code"].Value, message)];
    }

    [GeneratedRegex(@"^npm error (?<rest>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Error();

    [GeneratedRegex(@"^code (?<code>\S+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Code();
}
