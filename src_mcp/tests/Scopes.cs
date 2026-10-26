namespace CoaiMcp.Tests;

/// <summary>Several scopes, given back in reverse.</summary>
internal sealed class Scopes(params IDisposable[] scopes) : IDisposable
{
    public void Dispose()
    {
        foreach (var scope in scopes.Reverse())
        {
            scope.Dispose();
        }
    }
}
