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

    /// <summary>Requests a minute; zero means no limit.</summary>
    public int Value { get; }

    /// <summary>Whether the limiter is switched off.</summary>
    public bool Disabled => Value == 0;

    private RatePerMinute(int value) => Value = value;

    /// <summary>Reads the setting, or says why it cannot be used.</summary>
    public static Parsed Parse(string? raw)
    {
        // Only ABSENT is the default. `COAI_BUGS_RATE_PER_MINUTE=` in a unit's environment file is a
        // value somebody meant to fill in, and it is refused with the range rather than quietly
        // becoming 10. (Code round, gemini.)
        if (raw is null)
        {
            return new Parsed.Rate(new RatePerMinute(Default));
        }

        var digits = int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value);

        return digits && value <= Most
            ? new Parsed.Rate(new RatePerMinute(value))
            : new Parsed.Refused(
                $"{Variable} is '{raw}'; it must be a whole number from 0 (no limit) to {Most}, "
                + $"and unset means {Default}");
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
