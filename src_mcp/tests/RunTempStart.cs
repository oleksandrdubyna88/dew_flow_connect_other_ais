using System.Runtime.CompilerServices;
using Xunit;

[assembly: AssemblyFixture(typeof(CoaiMcp.Tests.RunTempFixture))]
[assembly: AssemblyFixture(typeof(CoaiMcp.Tests.Leftovers.LeftoversSweepFixture))]
[assembly: CoaiMcp.Tests.StartOfRunOrder]

namespace CoaiMcp.Tests;

/// <summary>A module initializer runs only when ITS OWN assembly loads (plan round-1 finding 2): every test assembly has this one.</summary>
internal static class RunTempStart
{
    [ModuleInitializer]
    internal static void Start() => RunTemp.Start();
}
