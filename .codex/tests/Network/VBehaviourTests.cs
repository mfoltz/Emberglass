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

    /// <summary>
    /// Ensures work scheduled after shutdown begins is not queued onto a detached invoker.
    /// </summary>
    [Fact]
    public void RunOnMainThreadIfAvailable_AfterInvokerClear_RunsInline()
    {
        var invoker = new QueueingMainThreadInvoker();
        bool wasRun = false;

        VBehaviour.MainThreadInvoker = invoker;
        VBehaviour.DrainAndClearMainThreadInvoker();

        VBehaviour.RunOnMainThreadIfAvailable(() => wasRun = true);

        Assert.True(wasRun);
        Assert.False(invoker.HasQueuedActions);
    }

    /// <summary>
    /// Ensures callback failures during shutdown drain do not prevent invoker cleanup.
    /// </summary>
    [Fact]
    public void DrainAndClearMainThreadInvoker_WhenCallbackThrows_StillClearsInvoker()
    {
        var invoker = new QueueingMainThreadInvoker();

        VBehaviour.MainThreadInvoker = invoker;
        invoker.Run(() => throw new InvalidOperationException("callback failed"));

        VBehaviour.DrainAndClearMainThreadInvoker();

        Assert.True(invoker.WasDrained);
        Assert.Null(VBehaviour.MainThreadInvoker);
    }

    /// <summary>
    /// Ensures one queued callback failure does not block later callbacks.
    /// </summary>
    [Fact]
    public void MainThreadInvoker_Drain_WhenCallbackThrows_ContinuesDraining()
    {
        var invoker = new MainThreadInvoker();
        bool secondCallbackRan = false;

        invoker.Run(() => throw new InvalidOperationException("callback failed"));
        invoker.Run(() => secondCallbackRan = true);

        invoker.Drain();

        Assert.True(secondCallbackRan);
    }

    sealed class QueueingMainThreadInvoker : IMainThreadInvoker
    {
        readonly Queue<Action> queuedActions = new();

        public bool IsMainThread => true;

        public bool HasQueuedActions => queuedActions.Count > 0;

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
