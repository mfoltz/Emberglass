using System.Collections.Concurrent;

namespace Emberglass.API.Shared;

/// <summary>
/// Provides a thread-safe main-thread invoker backed by a concurrent queue.
/// </summary>
public sealed class MainThreadInvoker : IMainThreadInvoker
{
    readonly ConcurrentQueue<Action> actionQueue = new();
    readonly int mainThreadId;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainThreadInvoker"/> class on the main thread.
    /// </summary>
    public MainThreadInvoker()
    {
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    /// <inheritdoc />
    public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == mainThreadId;

    /// <inheritdoc />
    public void Run(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        actionQueue.Enqueue(action);
    }

    /// <inheritdoc />
    public void Drain()
    {
        while (actionQueue.TryDequeue(out var action))
        {
            action();
        }
    }
}
