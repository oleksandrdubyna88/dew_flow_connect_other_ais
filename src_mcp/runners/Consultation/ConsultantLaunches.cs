using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The two guards every consultant launch shares, and the phrase test its refusals are read with.
/// </summary>
/// <remarks>
/// Extracted when the second adapter needed them rather than copied into it: a delivery rule that
/// holds in three adapters and is written in one is a rule; written three times it is three rules
/// that will disagree. The first real run of this product wrote both of them.
/// </remarks>
internal static class ConsultantLaunches
{
    /// <summary>A handle reaching an adapter unvalidated is a contract violation, not a runtime case.</summary>
    public static void MustBeLaunchable(ConsultantLaunch launch)
    {
        if (launch.Handle.Length > 0 && !ConsultantHandle.IsWellFormed(launch.Handle))
        {
            throw new ArgumentException(
                "a consultation handle must be validated before it is launched", nameof(launch));
        }
    }

    /// <summary>
    /// No argument value may carry a line break.
    /// </summary>
    /// <remarks>
    /// On Windows every vendor CLI here is an npm <c>.cmd</c> shim, so cmd.exe parses our argv and
    /// truncates an argument at its first newline — silently, so the model answers as if it had been
    /// handed nothing. Asserted over EVERY value, not only the prompt: a repository path, an output
    /// path and a configured model name are all somebody else's strings.
    /// </remarks>
    public static void MustCarryNoLineBreak(IReadOnlyList<string> arguments)
    {
        for (var at = 0; at < arguments.Count; at++)
        {
            if (arguments[at].Contains('\n') || arguments[at].Contains('\r'))
            {
                // The POSITION and a sanitised head, never the value verbatim. The offending value is
                // a configured model name or path and can carry `\r\n[a forged log line]`; anything
                // that logs this exception would then print it as its own. (codex, code round.)
                throw new ArgumentException(
                    $"consultation argument {at} contains a line break and would be truncated by a Windows shim: "
                    + $"'{OneLine(arguments[at])}'",
                    nameof(arguments));
            }
        }
    }

    /// <summary>A value safe to put in a message: control characters shown, and a bounded length.</summary>
    private static string OneLine(string value)
    {
        var flattened = string.Concat(value.Select(c => c switch
        {
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => char.IsControl(c) ? "?" : c.ToString(),
        }));

        return flattened.Length <= 120 ? flattened : flattened[..120] + "…";
    }

    /// <summary>Whether the process said this, on either stream, however it was cased.</summary>
    public static bool Mentions(ProcessResult result, string phrase) =>
        result.StdErr.Contains(phrase, StringComparison.OrdinalIgnoreCase)
        || result.StdOut.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
