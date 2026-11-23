namespace CoaiMcp.FakeCli;

/// <summary>
/// This program as the <c>coai-mcp</c> the installer's scenarios register: a COPY of FakeCli named <c>coai-mcp</c> (<c>coai-mcp.exe</c> on Windows), asked <c>--version</c> by the installer for every
/// binary that is not its own. The text it answers is in <c>coai-mcp.version</c> beside it, so two stand-ins can say two versions. A file named for the product that is a shell script cannot be started
/// on Windows, which is why this is a program.
/// </summary>
internal static class CoaiMcpStandIn
{
    /// <summary>The exit code when this process is such a copy and was asked <c>--version</c>; null for anything else, which the rest of the program answers.</summary>
    internal static int? Answer(string[] args)
    {
        if (args is not ["--version"] || Environment.ProcessPath is not { } self || !Path.GetFileNameWithoutExtension(self).Equals("coai-mcp", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var note = Path.Combine(Path.GetDirectoryName(self) ?? string.Empty, "coai-mcp.version");
        if (!File.Exists(note))
        {
            return null;
        }

        Console.Out.Write(File.ReadAllText(note));

        return 0;
    }
}
