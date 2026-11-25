using System.Text;
using CoaiMcp.Server.Keys;
using FluentAssertions;

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

/// <summary>The real <c>coai-mcp</c> binary run with its own data folder and a text on standard input — exactly as a person or a boot script runs it.</summary>
internal static class KeysBinary
{
    internal static (int Code, string Out, string Err) Run(string dataDir, string stdin, params string[] args)
    {
        var info = OneShotProcess.StartInfo(dataDir, args);
        info.RedirectStandardInput = true;
        using var process = System.Diagnostics.Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using (var input = process.StandardInput)
        {
            try
            {
                input.BaseStream.Write(Encoding.UTF8.GetBytes(stdin));
            }
            catch (IOException)
            {
                // The child refused a long line and closed its stdin first: that is the answer under test.
            }
        }

        process.WaitForExit(TimeSpan.FromMinutes(2)).Should().BeTrue();
        Task.WaitAll([output, error], 10_000).Should().BeTrue();

        return (process.ExitCode, output.Result, error.Result);
    }
}
