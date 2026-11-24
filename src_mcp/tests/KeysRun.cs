using System.Text;
using CoaiMcp.Server.Keys;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--keys</c> run in-process, the way the dispatch runs it, with the streams, the clock, the environment and the data folder of a <see cref="KeyWorld"/>: what it printed, what it said and how it exited.
/// </summary>
internal sealed class KeysRun(KeyWorld world)
{
    /// <summary>The environment variables the mode sees (<c>COAI_CLAUDE_KEY_*</c> and friends).</summary>
    internal Dictionary<string, string> Variables { get; } = [];

    /// <summary>The settings and environment lookup: the data folder of the world, and whatever a test set.</summary>
    internal Dictionary<string, string> Settings { get; } = [];

    internal bool Terminal { get; set; }

    internal string? Typed { get; set; }

    internal string Prompts { get; private set; } = string.Empty;

    /// <summary>Runs <c>--keys</c> with <paramref name="args"/> (after the mode name) and <paramref name="stdin"/> as the bytes on standard input.</summary>
    internal (int Code, string Out, string Err) Do(string stdin, params string[] args) => Do(Encoding.UTF8.GetBytes(stdin), args);

    internal (int Code, string Out, string Err) Do(byte[] stdin, params string[] args)
    {
        var output = new StringWriter();
        var said = new StringBuilder();
        var prompts = new StringBuilder();
        var runtime = new KeysRuntime(
            world.DataDir,
            name => name == "COAI_DATA_DIR" ? world.DataDir : Settings.GetValueOrDefault(name),
            Variables,
            new MemoryStream(stdin),
            Terminal,
            () => Typed,
            text => prompts.Append(text),
            output,
            sentence => said.AppendLine(sentence),
            world.Clock,
            world.Notices.Add,
            world.Seams ?? KeyFolderSeams.Real);
        var code = KeysMode.Run(args, runtime);
        Prompts = prompts.ToString();

        return (code, output.ToString(), said.ToString());
    }

    /// <summary>The rows of a <c>list --json</c>, as the page would read them.</summary>
    internal System.Text.Json.JsonElement List()
    {
        var listed = Do(string.Empty, "list", "--json");

        return System.Text.Json.JsonDocument.Parse(listed.Out).RootElement.Clone();
    }
}
