// Ported from wsl_care (MIT, same copyright holder): src_daemon/src/WslCare.Core/Files/IFileSystem.cs @ 0a5c72d3 — the closed union
// `FileReadResult` only (DL2 of todo/PLAN_kimi_and_vibe_on_linux.md), verbatim in shape.
namespace CoaiMcp.Runners.Files;

/// <summary>What reading a file produced: its bytes, nothing there, or a file that is there and cannot be read.</summary>
/// <remarks>
/// <para>Three facts, kept apart on purpose: a person who has not signed in has NO file (a launch goes on without a link), while a file
/// that is there and may not be trusted is a refusal the person must be told.</para>
/// <para><b>A <see cref="Content"/> is never interpolated into a string and never logged</b>: a positional record prints its members, and
/// these bytes may be a credential. Whoever receives one zeroes it after the scan.</para>
/// </remarks>
public abstract record FileReadResult
{
    private FileReadResult()
    {
    }

    public sealed record Content(byte[] Bytes) : FileReadResult;

    public sealed record Missing : FileReadResult;

    public sealed record Unreadable(string Reason) : FileReadResult;

    /// <summary>The reason a file past its cap gives.</summary>
    public static string TooLarge(int maxBytes) => $"larger than {maxBytes} bytes";
}
