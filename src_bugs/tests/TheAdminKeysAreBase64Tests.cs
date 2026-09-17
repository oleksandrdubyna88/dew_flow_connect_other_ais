using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The administrators arrive BASE64, always, and anything else is a refusal.
/// </summary>
/// <remarks>
/// <para><b>Why the variable is encoded at all.</b> It is newline-separated — one key per line, with
/// `#` comments so the operator can say whose is whose — and a systemd <c>EnvironmentFile</c>
/// assignment cannot hold a newline. The forced command that delivers it reads lines from stdin, so
/// the value has to survive as ONE line from the secret store to the unit file to this process. The
/// operator decided on 2026-09-17: base64 on the wire and base64 in the file, and no custom
/// separator — a second separator would put a second parsing rule into a credential boundary, and a
/// key containing it would silently become two keys that match nothing.</para>
/// <para><b>Why there is no fallback, and why "valid base64" is not the whole test.</b> A raw list
/// and an encoded one can both be non-empty text, so a server that tried base64 and fell back to raw
/// would turn a typo into a server running with the WRONG administrators. Worse, the two shapes
/// genuinely overlap: a 64-character hex key is inside the base64 alphabet and its length is a
/// multiple of four, and <see cref="System.Convert.FromBase64String"/> IGNORES whitespace — so a raw
/// two-line list can decode, silently, to nonsense. Three rules make them disjoint, and each one is
/// a test below: no whitespace in the encoded value, strict UTF-8 on the bytes, and no control
/// characters in the text. (Plan round, all three reviewers.)</para>
/// </remarks>
public sealed class TheAdminKeysAreBase64Tests
{
    private const string Secret = "a-server-secret";

    /// <summary>Text, encoded, on one line — with NO marker, for the cases that must be refused.</summary>
    private static string Encoded(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    /// <summary>What the deploy actually sends: the marker, then the list, encoded.</summary>
    private static string Marked(string list) =>
        Encoded($"{AdminKeys.Marker}\n{list}");

    /// <summary>The administrators a delivered value configures, or the reason it configures none.</summary>
    private static AdminKeys.Configured Read(string? delivered) => AdminKeys.Read(delivered, Secret);

    /// <summary>The administrators, when the value was usable. Fails the test when it was not.</summary>
    private static AdminKeys Administrators(string? delivered)
    {
        var read = Read(delivered);
        read.Should().BeOfType<AdminKeys.Configured.Admins>(
            $"'{delivered}' should have configured administrators, and instead: "
            + (read as AdminKeys.Configured.Refused)?.Why);

        return ((AdminKeys.Configured.Admins)read).Keys;
    }

    /// <summary>The sentence a value was refused with. Fails the test when it was accepted.</summary>
    private static string Refusal(string? delivered)
    {
        var read = Read(delivered);
        read.Should().BeOfType<AdminKeys.Configured.Refused>(
            $"'{delivered}' is not a value this server may start with");

        return ((AdminKeys.Configured.Refused)read).Why;
    }

    /// <summary>The ordinary case: what `base64 -w0` makes of a two-line list.</summary>
    [Fact]
    public void ABase64OfTwoKeysConfiguresTwoAdministrators()
    {
        var admins = Administrators(Marked("alices-key\nbobs-key"));

        admins.Count.Should().Be(2);
        admins.Match("alices-key", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>(
            "the key the operator wrote is the key the server must accept");
        admins.Match("bobs-key", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>();
    }

    /// <summary>The legibility the newlines were for is not lost; it travels encoded.</summary>
    [Fact]
    public void CommentsAndBlankLinesStillWorkInsideTheEncodedText()
    {
        var admins = Administrators(Marked("# alice, who runs the workshop\nalices-key\n\n# bob is away\n"));

        admins.Count.Should().Be(1);
        admins.Match("alices-key", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>();
        admins.Match("# alice, who runs the workshop", Secret).Should().BeOfType<AdminKeys.Presented.Unknown>(
            "a comment is a note to a person, and never reaches this server's memory as a credential");
    }

    /// <summary>An absent variable is no administrators, which is a legitimate way to run this server.</summary>
    [Fact]
    public void AnAbsentVariableConfiguresNobodyAndIsNotARefusal()
    {
        Administrators(null).None.Should().BeTrue();
    }

    /// <summary>And so is an empty one — the deploy writes the line even when there is nothing in it.</summary>
    [Fact]
    public void AnEmptyVariableConfiguresNobodyAndIsNotARefusal()
    {
        Administrators(string.Empty).None.Should().BeTrue();
        Administrators(Marked(string.Empty)).None.Should().BeTrue();
        Administrators(Marked("# nobody yet\n")).None.Should().BeTrue();
    }

    /// <summary>
    /// THE ONE THIS EXISTS FOR: the old raw shape, refused rather than mistaken for an administrator.
    /// </summary>
    /// <remarks>
    /// Before the encoding rule this value configured one administrator whose key was the whole
    /// blob, so every real credential was refused and the startup log said nothing — the server was
    /// configured, and wrong.
    /// </remarks>
    [Fact]
    public void ARawNewlineSeparatedListIsRefusedAndTheRefusalSaysHowToFixIt()
    {
        var why = Refusal("alices-key\nbobs-key");

        why.Should().Contain(AdminKeys.Variable, "a refusal names the variable to edit");
        why.Should().Contain("base64", "and the shape it must be in");
        why.Should().Contain("-w0", "and the command that produces it");
    }

    /// <summary>`base64` without `-w0` wraps at 76 columns, and that is the obvious mistake.</summary>
    [Fact]
    public void WrappedBase64IsRefusedBecauseTheWrapIsWhitespace()
    {
        var wrapped = Marked(new string('a', 120)).Insert(76, "\n");

        Refusal(wrapped).Should().Contain("-w0");
    }

    /// <summary>A Windows editor adds a carriage return, and the message has to name that too.</summary>
    [Fact]
    public void ATrailingCarriageReturnIsRefusedAndTheMessageNamesLineBreaks()
    {
        var why = Refusal(Marked("alices-key") + "\r\n");

        why.Should().ContainAny("line break", "carriage return");
    }

    /// <summary>Not base64 at all — a value nobody encoded.</summary>
    [Fact]
    public void SomethingThatIsNotBase64IsRefused()
    {
        Refusal("this is not base64!!").Should().Contain(AdminKeys.Variable);
    }

    /// <summary>
    /// The overlap that makes the first three rules insufficient: a hex key IS valid base64.
    /// </summary>
    /// <remarks>
    /// Sixty-four hex characters have no whitespace and decode cleanly to 48 bytes, and for most
    /// keys those bytes are not text — which is what rules 2 and 3 catch.
    /// </remarks>
    [Fact]
    public void ASingleRawKeyThatIsAccidentallyValidBase64IsRefused()
    {
        Refusal(new string('a', 64)).Should().Contain(AdminKeys.Variable);
    }

    /// <summary>
    /// THE ONE THE CODE ROUND FOUND: a raw key that survives every rule except the marker.
    /// </summary>
    /// <remarks>
    /// <para><c>aCE0</c> repeated sixteen times is a plausible 64-character key. It has no
    /// whitespace, it is valid base64, and it decodes to <c>h!4</c> repeated — printable ASCII, no
    /// control characters, perfectly good UTF-8. Every rule this suite had passed it, and it would
    /// have been configured as an administrator nobody holds.</para>
    /// <para>Worse, the deployment's own check would have said 200: the workflow decodes the same
    /// value and would have sent the same nonsense key, which IS the configured one. A check that
    /// agrees with the defect is the failure this story exists to prevent, so the fix cannot be a
    /// better check — it has to be a shape a raw key cannot have. (Code round, codex, twice.)</para>
    /// </remarks>
    [Fact]
    public void ARawKeyThatDecodesToPerfectlyGoodTextIsStillRefused()
    {
        var plausible = string.Concat(Enumerable.Repeat("aCE0", 16));

        // The trap, demonstrated rather than asserted about: it really is text.
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(plausible));
        decoded.Should().Be(string.Concat(Enumerable.Repeat("h!4", 16)), "this is the value that got through");

        Refusal(plausible).Should().Contain(AdminKeys.Marker, "and the refusal has to say what was missing");
    }

    /// <summary>An encoded list without the marker is refused, whatever else is right about it.</summary>
    [Fact]
    public void AnEncodedListWithoutTheMarkerIsRefused()
    {
        Refusal(Encoded("alices-key\nbobs-key")).Should().Contain(AdminKeys.Marker);
    }

    /// <summary>The marker has to be FIRST — a list that mentions it lower down is not marked.</summary>
    [Fact]
    public void TheMarkerIsTheFirstLineOrItIsNotOne()
    {
        Refusal(Encoded($"alices-key\n{AdminKeys.Marker}\n")).Should().Contain(AdminKeys.Marker);
    }

    /// <summary>And the refusal says what to type, because a marker nobody knows about is a trap.</summary>
    [Fact]
    public void TheRefusalShowsTheCommandThatProducesAValidValue()
    {
        var why = Refusal(Encoded("alices-key"));

        why.Should().Contain("base64 -w0");
        why.Should().Contain(AdminKeys.Marker);
        why.Should().Contain("printf", "a sentence about a format is worth less than the line to paste");
    }

    /// <summary>
    /// A byte-order mark is invisible and would otherwise become part of the first key.
    /// </summary>
    /// <remarks>
    /// A Windows editor writes one, the list looks exactly right, and the first administrator's key
    /// silently becomes BOM-plus-key — which matches nothing, while the deployment check, sending
    /// the same malformed value, would have answered 200. (Code round, codex.)
    /// </remarks>
    [Fact]
    public void AByteOrderMarkIsRefusedAndNamed()
    {
        var why = Refusal(Encoded($"\uFEFF{AdminKeys.Marker}\nalices-key\n"));

        why.Should().Contain("byte-order mark");
        why.Should().Contain("without a BOM");
    }

    /// <summary>
    /// A list written on Windows carries the marker with a carriage return, and that is still marked.
    /// </summary>
    /// <remarks>
    /// The first line is taken by splitting on a newline and trimming the end, so the carriage
    /// return a CRLF file leaves behind is trimmed with it. A reviewer asked about this case in the
    /// code round and answered it themselves mid-paragraph; it is pinned here so that the next
    /// reader does not have to.
    /// </remarks>
    [Fact]
    public void AMarkerWrittenWithWindowsLineEndingsIsStillTheMarker()
    {
        var admins = Administrators(Encoded($"{AdminKeys.Marker}\r\nalices-key\r\n"));

        admins.Count.Should().Be(1);
        admins.Match("alices-key", Secret).Should().BeOfType<AdminKeys.Presented.Administrator>();
    }

    /// <summary>The marker is a COMMENT, so nothing downstream had to learn about it.</summary>
    [Fact]
    public void TheMarkerIsNotItselfAnAdministrator()
    {
        var admins = Administrators(Marked("alices-key\n"));

        admins.Count.Should().Be(1, "the marker is dropped like every other comment");
        admins.Match(AdminKeys.Marker, Secret).Should().BeOfType<AdminKeys.Presented.Unknown>();
    }

    /// <summary>Bytes that are not UTF-8 are not a key list, whatever they decode from.</summary>
    [Fact]
    public void APayloadThatIsNotTextIsRefused()
    {
        Refusal(Convert.ToBase64String([0x80, 0x81, 0x82, 0xFF])).Should().Contain("text");
    }

    /// <summary>And text with control characters in it is not one either.</summary>
    [Fact]
    public void APayloadWithControlCharactersIsRefused()
    {
        Refusal(Marked("alices-key\u0000bobs-key")).Should().Contain("text");
    }

    /// <summary>Tabs and the line endings the format is MADE of are not control characters here.</summary>
    [Fact]
    public void TheLineEndingsInsideTheEncodedTextAreFine()
    {
        Administrators(Marked("alices-key\r\n\tbobs-key\r\n")).Count.Should().Be(2);
    }

    /// <summary>Every refusal names the variable, so the operator knows where to look.</summary>
    [Fact]
    public void EveryRefusalNamesTheVariable()
    {
        string[] unusable =
        [
            "alices-key\nbobs-key",
            "this is not base64!!",
            new string('a', 64),
            Convert.ToBase64String([0x80, 0x81, 0x82, 0xFF]),
            Marked("alices-key\u0000bobs-key"),
        ];

        foreach (var value in unusable)
        {
            Refusal(value).Should().Contain(AdminKeys.Variable, $"'{value}' was refused without saying what to edit");
        }
    }
}
