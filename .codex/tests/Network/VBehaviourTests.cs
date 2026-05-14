using System.Reflection;
using Emberglass.API.Shared;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers Unity lifecycle callback shape for the shared behavior component.
/// </summary>
public sealed class VBehaviourTests
{
    /// <summary>
    /// Ensures Unity can invoke the update loop on the component instance.
    /// </summary>
    [Fact]
    public void Update_IsInstanceLifecycleCallback()
    {
        MethodInfo? InstanceUpdate = typeof(VBehaviour).GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? StaticUpdate = typeof(VBehaviour).GetMethod(
            "Update",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(InstanceUpdate);
        Assert.Null(StaticUpdate);
    }
}
