namespace CoaiMcp.Server.Keys;

/// <summary>
/// One key as a person or a source hands it in, before it is a key: a label, the text, the account when known, where it came from, and the line it was on.
/// </summary>
/// <remarks>
/// A class with its own <see cref="ToString"/>, not a record: a record prints every member, and this one holds the text. It prints as <c>key input &lt;line&gt;</c>.
/// </remarks>
internal sealed class KeyInput(int line, string label, string token, string account, string source)
{
    public int Line { get; } = line;

    public string Label { get; } = label;

    public string Account { get; } = account;

    public string Source { get; } = source;

    /// <summary>The text, as given. Read by the pool when it checks and writes it, and by nothing else.</summary>
    internal string TokenText { get; } = token;

    public override string ToString() => $"key input {Line}";
}

/// <summary>What adding one input came to. A closed set; the refusal carries a sentence that never repeats the input.</summary>
internal abstract record AddOutcome
{
    private AddOutcome()
    {
    }

    public sealed record Added(KeyEntry Entry) : AddOutcome;

    public sealed record AlreadyThere(string Label) : AddOutcome;

    public sealed record Refused(string Reason, int Code) : AddOutcome;
}
