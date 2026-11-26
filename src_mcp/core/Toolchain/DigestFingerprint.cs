using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The fingerprint of a digest item (E4 s4 t4; SPEC C, read by the no-progress stop of section G and by the re-run of section F): SHA-256 over the step, the file, the code and the message — with digit runs and white space made equal, and WITHOUT the line number, so a line added above a
/// failure does not make it a new failure. Pure.
/// </summary>
public static partial class DigestFingerprint
{
    /// <summary>The 64 lower-case hex characters of the item's fingerprint.</summary>
    public static string Of(DigestItem item)
    {
        var text = string.Join('\u001f', item.Step, item.File, item.Code, Normalize(item.Message));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Normalize(string message)
    {
        try
        {
            return White().Replace(Digits().Replace(message, "0"), " ").Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return message.Length > 200 ? message[..200] : message;
        }
    }

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Digits();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex White();
}
