using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The proof that decides whether a capture may be reviewed as a committed plan: git's blob id, recomputed from the text
/// the launcher handed back. The ids are git's own, measured on real blobs.
/// </summary>
public sealed class ACaptureIsProvedToBeTheBlobTests
{
    private const string PlanOid = "707279fd268816a6390d22d1db277a907635e45f";
    private const string EmptyOid = "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391";
    private const string BomOid = "da68b30f1b6e09152fb89ef7f458b8c88fa8ddd5";
    private const string BadOid = "ab2a99ec86659da93ded257f43192597b1820798";

    private const string Pointer =
        "version https://git-lfs.github.com/spec/v1\noid sha256:4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393\nsize 12345\n";

    /// <summary>Git's blob id, computed here independently of the code under test.</summary>
    private static string GitId(byte[] bytes) =>
        Convert.ToHexStringLower(SHA1.HashData([.. Encoding.ASCII.GetBytes($"blob {bytes.Length}\0"), .. bytes]));

    [Fact]
    public void TheBlobIdOfAText_IsGits()
    {
        BlobId.Sha1Of("hello plan\n"u8).Should().Be(PlanOid);
        BlobId.Sha1Of(""u8).Should().Be(EmptyOid);
        BlobId.Sha1Of([0xEF, 0xBB, 0xBF, .. "bom text\n"u8]).Should().Be(BomOid);
    }

    [Fact]
    public void AValidCapture_IsProved_AndItsTextIsTheCapture()
    {
        var verdict = BlobProof.Prove("hello plan\n", PlanOid, 11);

        var proved = verdict.Should().BeOfType<ProofVerdict.Proved>().Subject;
        proved.Text.Should().Be("hello plan\n");
        proved.BomDropped.Should().BeFalse();
    }

    [Fact]
    public void ACaptureMissingTheBlobsBom_IsProvedOverTheBom_AndSaysSo()
    {
        // The launcher's decoder drops a leading BOM (measured): 9 characters come back for a 12-byte blob.
        var verdict = BlobProof.Prove("bom text\n", BomOid, 12);

        var proved = verdict.Should().BeOfType<ProofVerdict.Proved>().Subject;
        proved.Text.Should().Be("bom text\n");
        proved.BomDropped.Should().BeTrue();
    }

    [Fact]
    public void TwoLeadingBoms_AreProved_WithTheRemainingBomKeptInTheText()
    {
        var blob = new byte[] { 0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, (byte)'x' };

        var verdict = BlobProof.Prove("﻿x", GitId(blob), blob.Length);

        var proved = verdict.Should().BeOfType<ProofVerdict.Proved>().Subject;
        proved.Text.Should().Be("﻿x");
        proved.BomDropped.Should().BeTrue();
    }

    [Fact]
    public void ALiteralReplacementCharacter_IsProvedLikeAnyOtherCharacter()
    {
        // A VALID blob can hold U+FFFD (EF BF BD). The id matches, so the character is evidence of nothing.
        const string text = "café � end\n";
        var blob = Encoding.UTF8.GetBytes(text);

        var verdict = BlobProof.Prove(text, GitId(blob), blob.Length);

        verdict.Should().BeOfType<ProofVerdict.Proved>().Which.Text.Should().Be(text);
    }

    [Fact]
    public void ALossyDecode_IsNotUtf8()
    {
        // `abc \xFF\xFE def\n` (11 bytes) decodes to two U+FFFD and re-encodes to 15 bytes: neither id matches.
        var verdict = BlobProof.Prove("abc �� def\n", BadOid, 11);

        verdict.Should().BeOfType<ProofVerdict.Refused>().Which.Sentence.Should().Contain("not UTF-8");
    }

    [Fact]
    public void AWholeMismatchWithoutReplacement_DiffersFromTheBlob()
    {
        var verdict = BlobProof.Prove("hello plan", PlanOid, 11);

        verdict.Should().BeOfType<ProofVerdict.Refused>().Which.Sentence
            .Should().Contain("differs from the blob").And.Contain("10 bytes").And.Contain("11 declared");
    }

    [Fact]
    public void TheHeaderIsGitLfsSpecV1_AndOnlyAPrefixMatches()
    {
        LfsPointer.Header.Should().Be(Pointer.Split('\n')[0]);
        LfsPointer.IsPointer(Pointer).Should().BeTrue();
        LfsPointer.IsPointer("# Plan\n\nA pointer file begins with " + LfsPointer.Header + "\n").Should().BeFalse(
            "a plan that quotes the header is a plan");
        LfsPointer.IsPointer(string.Empty).Should().BeFalse();
    }
}
