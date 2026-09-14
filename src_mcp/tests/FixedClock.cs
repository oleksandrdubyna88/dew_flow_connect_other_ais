namespace CoaiMcp.Tests;

/// <summary>
/// A clock that says one thing, for a test that asserts a stamp.
/// </summary>
/// <remarks>
/// Five lines rather than a package: the family's NuGet policy wants a reason for every dependency,
/// and <c>Microsoft.Extensions.TimeProvider.Testing</c> would be carried by the whole solution so one
/// test file can name an instant. Advance it by assigning <see cref="Now"/> when a test needs two.
/// </remarks>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
