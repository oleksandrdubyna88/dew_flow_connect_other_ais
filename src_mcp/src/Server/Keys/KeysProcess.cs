using System.Text;

namespace CoaiMcp.Server.Keys;

/// <summary>The real streams of this process for <see cref="KeysMode"/>: stdin as bytes, a terminal read with no echo, the prompt on stderr and the answer on stdout.</summary>
internal static class KeysProcess
{
    /// <summary>Runs <c>--keys</c> as the process: <paramref name="args"/> are the arguments after the mode name.</summary>
    public static int Run(string[] args, Action<string> note) =>
        KeysMode.Run(
            args,
            new KeysRuntime(
                PanelSettings.DataDirectoryFor(Environment.GetEnvironmentVariable).Path,
                Environment.GetEnvironmentVariable,
                KeySourceSync.ProcessVariables(),
                Console.OpenStandardInput(),
                !Console.IsInputRedirected,
                ReadWithoutEcho,
                text => Console.Error.Write(text),
                Console.Out,
                note,
                TimeProvider.System,
                static _ => { },
                KeyFolderSeams.Real));

    /// <summary>One line from the terminal with the keys not shown; null when the input ended.</summary>
    internal static string? ReadWithoutEcho()
    {
        var typed = new StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && typed.Length > 0)
            {
                typed.Length--;
            }
            else if (!char.IsControl(key.KeyChar) && typed.Length < KeyStdin.MaxLineBytes)
            {
                typed.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();

        return typed.ToString();
    }
}
