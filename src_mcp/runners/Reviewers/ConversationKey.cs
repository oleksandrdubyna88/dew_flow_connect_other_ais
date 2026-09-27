using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The opaque key that names ONE reviewer's conversation to a vendor that routes its prompt cache by it.
/// </summary>
/// <remarks>
/// <para><b>A function of the reviewer and its base prompt, and of nothing else.</b> Every turn of a
/// feature reviewer's conversation resends the base prompt byte for byte with a tail appended (plan
/// §4.9, D25), and the repair of a turn is the same conversation asked again — so the key is taken from
/// the BASE, which every launch of the reviewer shares, never from a launch's whole prompt, which none
/// of them share. Two vendors in one round, or one vendor over two rounds, get two keys.</para>
/// <para><b>Why it exists — measured 2026-09-26.</b> xAI stores prompt-cache entries per server and
/// routes a request to the server holding them by the <c>x-grok-conv-id</c> header ("this maximizes
/// your cache hit rate", its prompt-caching guide). Three turns of a byte-identical 64 KB prefix without
/// the header cached 1,152 tokens each; the first trial saw the same on 14 of 17 follow-ups. The dialect
/// row names the header (<see cref="Core.Api.ApiDialect.CacheKeyHeader"/>); this is the value.</para>
/// <para>SHA-256 over the three parts, 32 hex characters: nothing of the prompt is readable from it, and
/// it is a stable id rather than a random one, so a retried round routes like the first.</para>
/// </remarks>
public static class ConversationKey
{
    public static string Of(string provider, string role, string basePrompt)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(provider + "\n" + role + "\n" + basePrompt));

        return Convert.ToHexStringLower(bytes)[..32];
    }
}
