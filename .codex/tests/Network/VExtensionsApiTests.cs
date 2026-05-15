using Emberglass.API.Shared;
using System.Reflection;
using Unity.Entities;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers the low-risk ECS extension helpers exposed for consumer mods.
/// </summary>
[Collection("Assembly setup")]
public sealed class VExtensionsApiTests
{
    /// <summary>
    /// Ensures common component and buffer helpers are part of the public extension surface.
    /// </summary>
    [Fact]
    public void VExtensions_ExposeSafeComponentAndBufferHelpers()
    {
        AssertExtension(nameof(VExtensions.With));
        AssertExtension(nameof(VExtensions.TryGetBuffer));
        AssertExtension(nameof(VExtensions.IsIndexWithinRange));
        AssertExtension(nameof(VExtensions.FirstOrDefault));
    }

    /// <summary>
    /// Ensures common entity lifecycle and player identity helpers are public extension methods.
    /// </summary>
    [Fact]
    public void VExtensions_ExposeLifecycleAndIdentityHelpers()
    {
        AssertExtension(nameof(VExtensions.Enable));
        AssertExtension(nameof(VExtensions.Disable));
        AssertExtension(nameof(VExtensions.IsUser));
        AssertExtension(nameof(VExtensions.GetUserEntity));
        AssertExtension(nameof(VExtensions.GetSteamId));
    }

    static void AssertExtension(string methodName)
    {
        Assert.Contains(
            typeof(VExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static),
            Method => Method.Name == methodName);
    }
}
