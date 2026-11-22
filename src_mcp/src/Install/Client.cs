namespace CoaiMcp.Install;

/// <summary>The three CLIs <c>--setup-clients</c> and <c>--install</c> know. Written in the order the output lists them.</summary>
internal enum Client
{
    Claude,
    Codex,
    Agy,
}

/// <summary>The words a person types for a <see cref="Client"/> (<c>--only claude,codex</c>) and the word the output carries.</summary>
internal static class Clients
{
    /// <summary>Every client, in output order.</summary>
    internal static readonly IReadOnlyList<Client> All = [Client.Claude, Client.Codex, Client.Agy];

    /// <summary>The word for <paramref name="client"/>: the CLI's own command name.</summary>
    internal static string Word(this Client client) => client switch
    {
        Client.Claude => "claude",
        Client.Codex => "codex",
        _ => "agy",
    };

    /// <summary>The client a word names, or none — an unknown word is the caller's 65, naming the legal ones.</summary>
    internal static Client? Parse(string word) =>
        All.Where(client => client.Word().Equals(word, StringComparison.Ordinal)).Select(client => (Client?)client).FirstOrDefault();

    /// <summary>The legal words, for the sentence that refuses an unknown one.</summary>
    internal static string Legal => string.Join(", ", All.Select(Word));
}
