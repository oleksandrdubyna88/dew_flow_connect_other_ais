using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Core.Collecting;

/// <summary>git's object id for a blob's bytes — the hash of <c>blob {length}\0</c> followed by the bytes. SHA-1 only.</summary>
/// <remarks>
/// A SHA-256 repository hashes its blobs with SHA-256 (measured), so this answers nothing about one; the reader refuses a
/// non-40-hex commit before it gets here rather than guess a repository's format from the length of one string.
/// </remarks>
public static class BlobId
{
    public static string Sha1Of(ReadOnlySpan<byte> bytes)
    {
        var header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        var whole = new byte[header.Length + bytes.Length];
        header.CopyTo(whole, 0);
        bytes.CopyTo(whole.AsSpan(header.Length));

        return Convert.ToHexStringLower(SHA1.HashData(whole));
    }
}

public abstract record ProofVerdict
{
    private protected ProofVerdict()
    {
    }

    /// <summary>The capture IS the blob. <paramref name="Text"/>: what the reviewers get. <paramref name="BomDropped"/>: the id matched only over EF BB BF + the bytes.</summary>
    public sealed record Proved(string Text, bool BomDropped) : ProofVerdict;

    public sealed record Refused(string Sentence) : ProofVerdict;
}

/// <summary>
/// Proves that the text a git read handed back is the blob git named — the one comparison that decides whether a
/// capture may be reviewed as the committed plan.
/// </summary>
/// <remarks>
/// <para>The launcher decodes stdout as UTF-8, lossily, and <b>drops a leading BOM</b> (measured on .NET 10). Re-encoding
/// the capture therefore gives the blob's bytes exactly when the blob was valid UTF-8 without a BOM; a blob that starts
/// with EF BB BF matches only with those three bytes put back. So the order is: the id over the bytes, then over BOM +
/// bytes, then refuse — and a literal U+FFFD in a VALID blob (<c>EF BF BD</c>) matches the FIRST id, which is why the
/// replacement character is looked for only after both ids have failed, never instead of them.</para>
/// <para>The sentences name the blob only; the reader that knows the path and commit puts them in front.</para>
/// </remarks>
public static class BlobProof
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public static ProofVerdict Prove(string captured, string oid, long size)
    {
        var bytes = Encoding.UTF8.GetBytes(captured);
        if (BlobId.Sha1Of(bytes) == oid)
        {
            return new ProofVerdict.Proved(captured, false);
        }

        if (BlobId.Sha1Of([.. Bom, .. bytes]) == oid)
        {
            return new ProofVerdict.Proved(captured, true);
        }

        return new ProofVerdict.Refused(captured.Contains('�') ? NotUtf8(oid) : Differs(oid, bytes.Length, size));
    }

    private static string NotUtf8(string oid) =>
        $"the file is not UTF-8 — the capture does not re-encode to blob {Short(oid)}; save the plan as UTF-8 and commit it";

    private static string Differs(string oid, int encoded, long size) =>
        $"the capture of blob {Short(oid)} differs from the blob — {encoded} bytes re-encoded against {size} declared; nothing was reviewed; re-run, and if it repeats report it";

    private static string Short(string id) => id[..Math.Min(12, id.Length)];
}
