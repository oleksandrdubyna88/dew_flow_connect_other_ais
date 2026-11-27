using CoaiMcp.Core.Work;

namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// The toolchain file of a repository: <c>.coai/toolchain.yaml</c>, read through <see cref="IYamlReader"/> and checked against <see cref="ToolchainTables"/> (server.md § 12). The strict YAML subset has block
/// lists and block maps and no flow list but <c>[]</c>, so a command is written one item to a line.
/// </summary>
/// <remarks>
/// <para><b>Absent is an answer.</b> No file is <see cref="ToolchainRead.NotConfigured"/> — never an error and never an empty project list; a file that is there and wrong is
/// <see cref="ToolchainRead.Refused"/> with EVERY fault, each naming the key it is about, so a person fixes the file once.</para>
/// <para><b>Pure</b>: the text arrives as a <see cref="WorkFile"/> — the file of a tree (<c>IWorkFolder</c>) or the blob of a commit — and the answer leaves as a value.</para>
/// </remarks>
public static class ToolchainFile
{
    /// <summary>Reads what a tree or a commit holds at <see cref="ToolchainTables.FilePath"/>.</summary>
    public static ToolchainRead Read(WorkFile file, IYamlReader reader) => file switch
    {
        WorkFile.Missing => new ToolchainRead.NotConfigured(),
        WorkFile.Text text => FromText(text.Content, reader),
        WorkFile.TooLarge large => RefusedWith($"the toolchain file is {large.Bytes} bytes; at most {ToolchainTables.MaxBytes} are read"),
        WorkFile.NotRegular odd => RefusedWith($"the toolchain file is not a regular file that can be read ({odd.Reason})"),
        _ => RefusedWith("the toolchain file is not UTF-8 text"),
    };

    /// <summary>The same, with the shipped strict reader.</summary>
    public static ToolchainRead Read(WorkFile file) => Read(file, new StrictYamlReader());

    private static ToolchainRead FromText(string text, IYamlReader reader) => reader.Read(text) switch
    {
        YamlRead.Parsed parsed => ToolchainParse.Run(parsed.Root),
        YamlRead.Refused refused => RefusedWith($"{ToolchainTables.FilePath} line {refused.Line}: {refused.Message} ({refused.Code})"),
        _ => RefusedWith("the toolchain file could not be read"),
    };

    private static ToolchainRead.Refused RefusedWith(string fault) => new([fault]);
}
