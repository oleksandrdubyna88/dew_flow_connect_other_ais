using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server.Keys;

namespace CoaiMcp.Tests;

/// <summary>
/// A claude key pool over a temporary data folder with a clock the test moves and the notices it wrote — the bed of the key pool tests (epoch 3 E2 s7).
/// </summary>
/// <remarks>
/// <para><b>Every token here is made up and built at run time from parts</b>, so no token-shaped literal is in this source (the repository's own scanners stay quiet, and a test name or a failure message cannot hold one).
/// Every made-up token carries <see cref="Sentinel"/>: a test that asserts the sentinel is absent from an output proves that no part of any key reached it, not only the whole text.</para>
/// <para>Temp data folders only; nothing here reads the real home or the real data folder.</para>
/// </remarks>
internal sealed class KeyWorld : IDisposable
{
    /// <summary>A word in every made-up token: found in an output, a key leaked there.</summary>
    internal const string Sentinel = "CANARYSENTINEL";

    private readonly TempDir _data = TempDir.For("coai-keys-");

    internal KeyWorld(KeyFolderSeams? seams = null, bool shared = false)
    {
        Seams = seams;
        Shared = shared;
        Clock = new KeyClock(new DateTimeOffset(2026, 11, 24, 8, 0, 0, TimeSpan.Zero));
        Admin = new KeyAdmin(Access());
    }

    internal string DataDir => _data.Path;

    internal string KeysRoot => Path.Combine(DataDir, "keys", "claude");

    internal KeyClock Clock { get; }

    internal KeyFolderSeams? Seams { get; }

    internal List<ServerNotice> Notices { get; } = [];

    internal KeyAdmin Admin { get; }


    /// <summary>Whether the device setting that makes the folder a shared instance is set (it still needs the setgid bit on the data folder).</summary>
    internal bool Shared { get; }

    internal KeyStoreAccess Access() => new(DataDir, Clock, Notices.Add, Seams, Shared);

    /// <summary>A made-up setup-token shaped like a real one: the prefix, the sentinel, the tag and a filler to 60 characters.</summary>
    internal static string Token(string tag) => string.Concat("sk-ant-", "oat01-", Sentinel, tag, new string('x', 60 - 13 - Sentinel.Length - tag.Length));

    /// <summary>The id the spec gives a token, computed here from the spec's words and not by the product's function.</summary>
    internal static string IdOf(string token) =>
        "k-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("coai-claude-key-v1\n" + token)))[..12];

    internal KeyRow AddKey(string label, string tag, string account = "", string source = KeySources.Local, TimeSpan? step = null)
    {
        Clock.Advance(step ?? TimeSpan.FromSeconds(1));
        var added = Admin.Add(new KeyInput(1, label, Token(tag), account, source));

        return added.Value ?? throw new InvalidOperationException($"the test key '{label}' was refused: {added.Refusal}");
    }

    internal string KeyFile(string tag) => Path.Combine(KeysRoot, IdOf(Token(tag)) + ".key");

    internal string MetaFile(string tag) => Path.Combine(KeysRoot, IdOf(Token(tag)) + ".json");

    public void Dispose() => _data.Dispose();
}

/// <summary>A clock a test moves by hand.</summary>
internal sealed class KeyClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    internal DateTimeOffset Now => _now;

    internal void Advance(TimeSpan by) => _now += by;
}
