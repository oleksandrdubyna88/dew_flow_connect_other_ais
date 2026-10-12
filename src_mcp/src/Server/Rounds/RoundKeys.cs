using System.Security.Cryptography;

namespace CoaiMcp.Server;

/// <summary>
/// The identity a round carries across the wire (story 4.1 of todo/PLAN_every_round_is_counted.md): 128 random bits
/// as 32 lower-case hex characters, minted once at the round's start, persisted on the round (the session file and
/// <c>rounds.round_key</c>) and sent to a Team server on every launch of the round — retries, repairs and follow-ups
/// included — so the server can count a round as ONE round however many launches it took.
/// </summary>
/// <remarks>
/// <para><b>Random, not a hash.</b> <c>sessionId</c> is eight hex characters and a round's number restarts with the
/// budget counter (<c>again</c>, the escalation ladder, a person's Continue/Fix), so a hash of the two would collide
/// across sessions and add no privacy. <c>again</c>, Continue/Fix and escalation start a new round and so a new key.</para>
/// <para>Not the <c>(session, stage, number)</c> triple the log calls a round key (<c>RoundKeyDto</c>, <c>RoundKeyAsked</c>):
/// that names a round's place in a journal; this names the round itself.</para>
/// </remarks>
internal static class RoundKeys
{
    /// <summary>Thirty-two hex characters: 128 bits.</summary>
    public const int Length = 32;

    /// <summary>A fresh key — from the cryptographic generator, so two rounds anywhere never share one.</summary>
    public static string Mint() => RandomNumberGenerator.GetHexString(Length, lowercase: true);

    /// <summary>Whether <paramref name="key"/> is a key this build mints: exactly <see cref="Length"/> lower-case hex characters.</summary>
    public static bool IsWellFormed(string key) =>
        key.Length == Length && key.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
