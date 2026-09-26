namespace CoaiMcp.Core.Rounds;

/// <summary>What a reviewer actually holds while it reviews — said to it once, by the code that knows.</summary>
/// <remarks>
/// <para>It was a bool, <c>hasCheckout</c>, which could say "a repository is mounted" or "the material
/// below and nothing else". The feature review is a third truth (plan §4.6): the reviewer has neither a
/// checkout nor the change itself, but an OUTLINE of the code and the changed hunks, and a way to ask for
/// source that no other stage offers. A bool could not say that, and telling a feature reviewer it had
/// "the change" would be the same kind of false claim the checkout sentence was removed for.</para>
/// </remarks>
public enum ReaderMaterial
{
    /// <summary>A READ-ONLY checkout in the working directory, and the material in the prompt.</summary>
    Checkout,

    /// <summary>The material in the prompt — the change, the plan, the rules — and nothing else.</summary>
    Change,

    /// <summary>The material in the prompt, whose code is an outline with the changed hunks — the feature review.</summary>
    Outline,
}
