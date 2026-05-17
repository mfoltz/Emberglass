using System.Reflection;
using BepInEx;
using Emberglass.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the defensive result surface for runtime plugin hotloading.
/// </summary>
public sealed class HotloadPluginResultTests
{
    /// <summary>
    /// Ensures hotloading can reuse an already loaded candidate assembly for retry attempts.
    /// </summary>
    [Fact]
    public void HotloadPluginResult_ReusesAlreadyLoadedCandidateAssembly()
    {
        string assemblyPath = typeof(Transference).Assembly.Location;

        Assembly assembly = Transference.ResolveHotloadAssemblyForTesting(
            assemblyPath,
            out bool reusedLoadedAssembly);

        Assert.Same(typeof(Transference).Assembly, assembly);
        Assert.True(reusedLoadedAssembly);
    }

    /// <summary>
    /// Ensures duplicate plugin GUID detection can see the currently loaded Emberglass plugin.
    /// </summary>
    [Fact]
    public void HotloadPluginResult_DetectsLoadedPluginGuid()
    {
        BepInPlugin metadata = typeof(Plugin).GetCustomAttribute<BepInPlugin>()!;

        Assert.True(Transference.IsPluginGuidLoadedForTesting(metadata.GUID));
    }
}
