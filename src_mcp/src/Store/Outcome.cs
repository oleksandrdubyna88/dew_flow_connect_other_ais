namespace CoaiMcp.Store;

/// <summary>
/// An expected failure as a value (the C# doctrine §5): a result, or the sentence that says why there is none. The round-authors report's one
/// outcome shape — its argument parsing, its <c>--add</c> read and its merge all answer with it, so no call site invents a
/// <c>(bool ok, T value, string why)</c> tuple of its own.
/// </summary>
/// <remarks><see cref="Value"/> is meaningful only when <see cref="Ok"/> is true; a refusal carries the type's default there.</remarks>
public readonly record struct Outcome<T>(bool Ok, T Value, string Why)
{
    public static Outcome<T> Of(T value) => new(true, value, string.Empty);

    public static Outcome<T> Refuse(string why) => new(false, default!, why);

    /// <summary>The next step on a result; a refusal passes through unchanged.</summary>
    public Outcome<TNext> Then<TNext>(Func<T, Outcome<TNext>> next) => Ok ? next(Value) : Outcome<TNext>.Refuse(Why);
}
