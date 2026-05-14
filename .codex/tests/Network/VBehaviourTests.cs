using System.Reflection;
using Emberglass.API.Shared;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers shared MonoBehaviour lifecycle helpers.
/// </summary>
[Collection("Assembly setup")]
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

    /// <summary>
    /// Ensures queued main-thread work is not abandoned during shutdown.
    /// </summary>
    [Fact]
    public void Uninitialize_DrainsMainThreadInvokerBeforeClearingIt()
    {
        var invoker = new QueueingMainThreadInvoker();
        bool wasDrained = false;

        VBehaviour.MainThreadInvoker = invoker;
        invoker.Run(() => wasDrained = true);

        VBehaviour.DrainAndClearMainThreadInvoker();

        Assert.True(wasDrained);
        Assert.True(invoker.WasDrained);
        Assert.Null(VBehaviour.MainThreadInvoker);
    }

    sealed class QueueingMainThreadInvoker : IMainThreadInvoker
    {
        readonly Queue<Action> queuedActions = new();

        public bool IsMainThread => true;

        public bool WasDrained { get; private set; }

        public void Run(Action action)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            queuedActions.Enqueue(action);
        }

        public void Drain()
        {
            WasDrained = true;
            while (queuedActions.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}
