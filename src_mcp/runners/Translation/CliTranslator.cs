using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Translation;

/// <summary>Translating a question, or saying plainly that it could not be translated.</summary>
public interface ITranslator
{
    Task<Translated> TranslateAsync(string text, Language target, string kind, CancellationToken ct = default);
}

/// <summary>Which fast model does the translating.</summary>
public sealed record TranslatorSettings(string Provider = "gemini")
{
    /// <summary>Empty = the CLI's own default. A flash/mini model is the point: this is a
    /// one-sentence job in front of a person who is waiting.</summary>
    public string Model { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Translation through a small, fast CLI model — Gemini Flash by default, Codex with a mini model
/// as the alternative.
/// </summary>
/// <remarks>
/// <para><b>Failure never hides the text.</b> A missing CLI, a timeout, a non-zero exit, an empty
/// answer: each returns the ORIGINAL with a note saying why, and the note reaches the person. A
/// question shown in the wrong language is a nuisance; a question silently replaced by an error
/// is a review that stops.</para>
/// <para>The text rides on <b>stdin</b>, like every other prompt here — on Windows these CLIs are
/// npm <c>.cmd</c> shims, and cmd.exe truncates a multi-line argument at its first newline.</para>
/// </remarks>
public sealed class CliTranslator(IProcessLauncher launcher, TranslatorSettings settings) : ITranslator
{
    public async Task<Translated> TranslateAsync(string text, Language target, string kind, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Translated.Untouched(text, string.Empty);
        }

        if (settings.Provider.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return Translated.Untouched(text, "translation is switched off");
        }

        var request = Build(text, target, kind);
        ProcessResult result;
        try
        {
            result = await launcher.RunAsync(request, ct);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return Translated.Untouched(text, $"the {settings.Provider} CLI could not be started ({e.Message})");
        }

        if (result.TimedOut)
        {
            return Translated.Untouched(text, $"the {settings.Provider} CLI timed out");
        }

        if (result.ExitCode != 0)
        {
            return Translated.Untouched(text, $"the {settings.Provider} CLI exited {result.ExitCode}");
        }

        var translated = Clean(Extract(result.StdOut));
        return translated.Length == 0
            ? Translated.Untouched(text, $"the {settings.Provider} CLI answered nothing")
            : new Translated(translated, text, string.Empty);
    }

    private ProcessRequest Build(string text, Language target, string kind)
    {
        var instruction = TranslationPrompt.For(target, kind);
        var executable = settings.ExecutablePath.Length > 0
            ? settings.ExecutablePath
            : settings.Provider.Equals("codex", StringComparison.OrdinalIgnoreCase) ? "codex" : "gemini";

        // The instruction and the text travel together on stdin; the flag arguments never carry
        // either, so nothing can be truncated at a newline.
        var payload = $"{instruction}\n\n---\n{text}";

        return settings.Provider.Equals("codex", StringComparison.OrdinalIgnoreCase)
            ? new ProcessRequest(
                executable,
                [
                    "exec", "-s", "read-only", "--ephemeral", "--skip-git-repo-check", "--color", "never",
                    .. settings.Model.Length > 0 ? (string[])["-m", settings.Model] : [],
                    "-",
                ],
                Environment.CurrentDirectory)
            {
                StdIn = payload,
                Timeout = settings.Timeout,
            }
            : new ProcessRequest(
                executable,
                [
                    "-p", "Follow the translation instruction above and output only the translation.",
                    "-o", "json",
                    "--skip-trust",
                    "--approval-mode", "plan",
                    .. settings.Model.Length > 0 ? (string[])["-m", settings.Model] : [],
                ],
                Environment.CurrentDirectory)
            {
                StdIn = payload,
                Timeout = settings.Timeout,
            };
    }

    /// <summary>Gemini answers inside its own envelope; codex writes the message to stdout.</summary>
    internal static string Extract(string stdout)
    {
        var trimmed = stdout.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("response", out var response) &&
                    response.ValueKind == JsonValueKind.String)
                {
                    return response.GetString() ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // Not an envelope — plain text that happens to start with a brace.
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Strips what a chatty model adds around an answer: a fenced block, or wrapping quotes. Both
    /// were asked against in the prompt; neither instruction is reliable enough to trust alone.
    /// </summary>
    internal static string Clean(string text)
    {
        var value = text.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = value.IndexOf('\n');
            var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
            if (firstBreak > 0 && lastFence > firstBreak)
            {
                value = value[(firstBreak + 1)..lastFence].Trim();
            }
        }

        if (value.Length > 1 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1].Trim();
        }

        return value;
    }
}
