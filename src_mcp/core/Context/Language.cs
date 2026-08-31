namespace CoaiMcp.Core.Context;

/// <summary>
/// The languages a person may choose to be asked in. Five, named in English AND in themselves —
/// a translator prompt written in the target language's own name gets better results than a code.
/// </summary>
public sealed record Language(string Code, string EnglishName, string NativeName)
{
    public static readonly Language English = new("en", "English", "English");
    public static readonly Language Spanish = new("es", "Spanish", "español");
    public static readonly Language German = new("de", "German", "Deutsch");
    public static readonly Language Russian = new("ru", "Russian", "русский");
    public static readonly Language Ukrainian = new("uk", "Ukrainian", "українська");

    public static readonly IReadOnlyList<Language> All = [English, Spanish, German, Russian, Ukrainian];

    /// <summary>
    /// The language for a code, or English. An unknown code is NOT an error worth failing a
    /// review over — a question in English is still a question; a refused escalation is not.
    /// </summary>
    public static Language For(string? code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? English;
}

/// <summary>What translation did to a piece of text, and why, when it did nothing.</summary>
/// <param name="Text">What to show: the translation, or the original when there is none.</param>
/// <param name="Original">Always the text as it arrived.</param>
/// <param name="Note">Empty when <paramref name="Text"/> is in the target language; otherwise why not.</param>
public sealed record Translated(string Text, string Original, string Note)
{
    public bool WasTranslated => Note.Length == 0 && !ReferenceEquals(Text, Original) && Text != Original;

    /// <summary>Nothing was done, and the reason is carried rather than hidden.</summary>
    public static Translated Untouched(string text, string note) => new(text, text, note);
}

/// <summary>
/// The instruction a small, fast model is given. Pure, so the wording is a test rather than
/// something tuned by watching one output.
/// </summary>
public static class TranslationPrompt
{
    /// <summary>
    /// Deliberately strict about the output: a translator that explains itself, apologises, or
    /// wraps the answer in quotes produces a modal that reads like a machine. And it must return
    /// text UNCHANGED when it is already in the target language — that is the common case, since
    /// a person usually configures the language the AI already writes in.
    /// </summary>
    public static string For(Language target, string kind) =>
        $"""
        Translate the {kind} below into {target.EnglishName} ({target.NativeName}).

        Rules, all of them absolute:
        - If the text is ALREADY in {target.EnglishName}, return it completely unchanged.
        - Output ONLY the translation. No preface, no explanation, no quotation marks around it,
          no notes about what you did.
        - Keep code, file paths, identifiers, numbers and punctuation exactly as they are.
        - Keep the meaning precise: this text decides whether someone ships code.
        """;
}
