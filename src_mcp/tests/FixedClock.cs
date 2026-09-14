namespace CoaiMcp.Tests;

/// <summary>
/// A clock that says one thing, for a test that asserts a stamp.
/// </summary>
/// <remarks>
/// Five lines rather than a package: the family's NuGet policy wants a reason for every dependency,
/// and <c>Microsoft.Extensions.TimeProvider.Testing</c> would be carried by the whole solution so one
/// test file can name an instant. A test that needs two moments makes two clocks: the value is
/// read-only, so no test can depend on having mutated its own fixture. (Code round, codex.)
/// </remarks>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset Now { get; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
