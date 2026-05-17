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
    /// Ensures hotloading an assembly that is already loaded is reported as a guarded failure.
    /// </summary>
    [Fact]
    public void HotloadPluginResult_RejectsAlreadyLoadedAssemblyPath()
    {
        string assemblyPath = typeof(Transference).Assembly.Location;

        HotloadPluginResult result = Transference.TryLoadPluginForTesting(assemblyPath);

        Assert.Equal(HotloadPluginStatus.AssemblyAlreadyLoaded, result.Status);
        Assert.False(result.Success);
        Assert.Contains(typeof(Transference).Assembly.GetName().Name!, result.Message);
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
