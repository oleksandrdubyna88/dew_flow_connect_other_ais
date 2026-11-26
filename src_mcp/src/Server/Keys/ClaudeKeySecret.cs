using System.Text;

namespace CoaiMcp.Server.Keys;

/// <summary>
/// The text of one pool key, as a type that cannot print itself (KEY_POOL_SPEC.md section 6).
/// </summary>
/// <remarks>
/// <para><b>Not a <c>string</c> property of a record</b>: a record prints its members. <see cref="ToString"/> is <c>claude key &lt;id&gt;</c>, no JSON context holds this type, and no log or console call takes it
/// (a scan test says so, with a companion that proves the scan sees something).</para>
/// <para><b>ONE member gives the text</b> — <see cref="Reveal"/>. Its only caller is the launch road (<c>RoleModelLauncher</c>, story t5), which puts it in the child's environment as <c>CLAUDE_CODE_OAUTH_TOKEN</c>
/// and nowhere else; a scan test fails when a second caller appears. The other uses of the text — replacing it in what a launch printed, finding it in a file — are members of THIS class
/// (<see cref="Scrub"/>, <see cref="IsIn"/>), so none needs a second caller of <see cref="Reveal"/>.</para>
/// </remarks>
internal sealed class ClaudeKeySecret
{
    private readonly string _text;
    private readonly byte[] _bytes;

    internal ClaudeKeySecret(string id, string text)
    {
        Id = id;
        _text = text;
        _bytes = Encoding.UTF8.GetBytes(text);
    }

    /// <summary>The key's id: not a secret.</summary>
    public string Id { get; }

    /// <summary>What a scrub writes where the text was: names the key, never the text.</summary>
    public string Marker => $"[claude key {Id}]";

    /// <summary>How many bytes a search window must overlap by so a text cut by a chunk edge is still found.</summary>
    public int ByteLength => _bytes.Length;

    /// <summary>The text. The one member that gives it.</summary>
    public string Reveal() => _text;

    /// <summary>The same output with every occurrence of this key's exact text replaced by <see cref="Marker"/>.</summary>
    public string Scrub(string output) => output.Replace(_text, Marker, StringComparison.Ordinal);

    /// <summary>The same output with every occurrence of this text replaced by a mark that names it as a SIGN-IN token of a vendor's copy — for a secret that is not a claude pool key, whose mark must not say "claude key".</summary>
    public string ScrubAsSignInToken(string output) => output.Replace(_text, $"[sign-in copy {Id}]", StringComparison.Ordinal);

    /// <summary>Whether the exact text of this key is inside <paramref name="bytes"/>.</summary>
    public bool IsIn(ReadOnlySpan<byte> bytes) => bytes.IndexOf(_bytes) >= 0;

    public override string ToString() => $"claude key {Id}";
}
