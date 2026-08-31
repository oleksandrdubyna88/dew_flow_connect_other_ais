using System.Reflection;
using Xunit;
using CoaiMcp.Core.Findings;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// The core stays pure — the epic-02 DoD line, held by a test instead of a habit. Process and
/// network use surface as distinct assembly references, so their absence is checkable; filesystem
/// types live inside the core library set and are held out by review instead.
/// </summary>
public sealed class ArchitectureTests
{
    [Fact]
    public void Core_ReferencesNoProcessOrNetworkAssemblies()
    {
        var references = typeof(Finding).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        references.Should().NotContain(
            ["System.Diagnostics.Process", "System.Net.Http", "System.Net.Sockets", "System.Net.Primitives"],
            "the pure core must not spawn, call, or listen — that is what the runners are for");
    }
}
