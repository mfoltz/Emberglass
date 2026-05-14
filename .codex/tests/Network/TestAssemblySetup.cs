using Emberglass.TestStubs;
using System.Runtime.CompilerServices;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Initializes assembly stubbing for test execution.
/// </summary>
public sealed class TestAssemblySetup
{
    /// <summary>
    /// Initializes runtime assembly stubs before any test method references Emberglass runtime-only types.
    /// </summary>
    [ModuleInitializer]
    public static void InitializeAssemblyStubs()
    {
        StubAssemblyResolver.Initialize();
    }

    /// <summary>
    /// Sets up the stubbed assembly resolver for the test run.
    /// </summary>
    public TestAssemblySetup()
    {
        InitializeAssemblyStubs();
    }
}

/// <summary>
/// Declares the shared test collection that ensures stub initialization runs once.
/// </summary>
[CollectionDefinition("Assembly setup")]
public sealed class AssemblySetupCollection : ICollectionFixture<TestAssemblySetup>
{
}
