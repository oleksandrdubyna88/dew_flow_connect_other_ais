using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Consultation;

/// <summary>
/// The vendor's own id for a conversation — untrusted BY POSITION, and checked at both ends.
/// </summary>
/// <remarks>
/// A handle arrives on another process's stdout and leaves in an argv that, on Windows, goes through
/// <c>cmd.exe</c> (every vendor here is an npm <c>.cmd</c> shim). The C# twin of
/// <c>codexAdapter.ts:42</c>, with the same shape and the same reason: "safe by construction" is a
/// proof the next reader has to reconstruct, so the guard runs where the handle is READ off the
/// stream and again where it is WRITTEN into a command line.
/// </remarks>
public static partial class ConsultantHandle
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$")]
    private static partial Regex WellFormed();

    public static bool IsWellFormed(string? handle) => handle is not null && WellFormed().IsMatch(handle);
}
