using System.Globalization;

namespace CoaiBugs;

/// <summary>
/// How many requests a minute one key may make — a setting, validated once at startup.
/// </summary>
/// <remarks>
/// <para><c>COAI_BUGS_RATE_PER_MINUTE</c>; default <see cref="Default"/> when the variable is ABSENT
/// — a present-but-blank one is a value, and is refused like any other that is not a whole number;
/// <c>0</c> DISABLES the limit, and the disabling is tested, because a limit nobody can switch off
/// takes the service down at 03:00.</para>
/// <para><b>Capped at <see cref="Most"/>, and a value past the cap is REFUSED rather than clamped.</b>
/// A reviewer computed that an unvalidated rate times the caller ceiling is a billion timestamps in
/// memory. A clamp would be a silent fallback; the doctrine says an illegal value fails naming the
/// legal ones, so the server exits 78 with the range in the message. Digits only: a sign, a space or
/// a word is refused the same way.</para>
/// </remarks>
public sealed record RatePerMinute
{
    /// <summary>The environment variable it is read from.</summary>
    public const string Variable = "COAI_BUGS_RATE_PER_MINUTE";

    /// <summary>What an unset variable means.</summary>
    public const int Default = 10;

    /// <summary>The largest value accepted.</summary>
    public const int Most = 1_000;

    /// <summary>
    /// WHICH limit is being read — there are two surfaces and they cannot share one number.
    /// </summary>
    /// <remarks>
    /// <para>The type is WIDENED rather than copied: the validation, the cap, the refusal sentence
    /// and the `0`-disables rule are identical for both, and a second near-identical setting type
    /// would be two places to fix the next time one of those rules changes.</para>
    /// <para><b>Why two numbers at all.</b> The contributor limit is flood control on a public
    /// endpoint, and 10 a minute is deliberately tight. The admin surface is a closed set of
    /// authenticated people driving a UI, and the arithmetic nobody had done says the tight number
    /// makes that UI unusable: the audit retains 50 000 rows and pages at most 200, so reading it is
    /// 250 requests — a `429` after ten pages and 25 minutes for the table. Raising the shared
    /// default was the wrong trade, because it would loosen flood control to suit a UI.</para>
    /// </remarks>
    public sealed record Surface(string Variable, int Default)
    {
        /// <summary>A contributor key on <c>/ingest</c>.</summary>
        public static readonly Surface Contributor = new(RatePerMinute.Variable, RatePerMinute.Default);

        /// <summary>An administrator on <c>/admin/*</c>.</summary>
        public static readonly Surface Administrator = new("COAI_BUGS_ADMIN_RATE_PER_MINUTE", 120);
    }

    /// <summary>Requests a minute; zero means no limit.</summary>
    public int Value { get; }

    /// <summary>Whether the limiter is switched off.</summary>
    public bool Disabled => Value == 0;

    private RatePerMinute(int value) => Value = value;

    /// <summary>Reads the contributor setting, or says why it cannot be used.</summary>
    public static Parsed Parse(string? raw) => Parse(raw, Surface.Contributor);

    /// <summary>Reads one of the two settings, or says why it cannot be used.</summary>
    /// <param name="raw">The variable's value, or <c>null</c> when it is absent.</param>
    /// <param name="surface">Which limit this is — it decides the name in the message and the default.</param>
    public static Parsed Parse(string? raw, Surface surface)
    {
        // Only ABSENT is the default. `COAI_BUGS_RATE_PER_MINUTE=` in a unit's environment file is a
        // value somebody meant to fill in, and it is refused with the range rather than quietly
        // becoming 10. (Code round, gemini.)
        if (raw is null)
        {
            return new Parsed.Rate(new RatePerMinute(surface.Default));
        }

        var digits = int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value);

        return digits && value <= Most
            ? new Parsed.Rate(new RatePerMinute(value))
            : new Parsed.Refused(
                $"{surface.Variable} is '{raw}'; it must be a whole number from 0 (no limit) to "
                + $"{Most}, and unset means {surface.Default}");
    }

    /// <summary>What reading the setting came to: a rate, or the reason there is none.</summary>
    public abstract record Parsed
    {
        private Parsed()
        {
        }

        /// <summary>A usable rate.</summary>
        public sealed record Rate(RatePerMinute Value) : Parsed;

        /// <summary>A value the server will not start with, and the sentence that says so.</summary>
        public sealed record Refused(string Why) : Parsed;
    }
}
