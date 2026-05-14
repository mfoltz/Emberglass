namespace Emberglass.API.Shared;

/// <summary>
/// Defines a main-thread invoker for scheduling and executing queued work.
/// </summary>
public interface IMainThreadInvoker
{
    /// <summary>
    /// Gets a value indicating whether the caller is on the main thread.
    /// </summary>
    bool IsMainThread { get; }

    /// <summary>
    /// Enqueues work to be executed on the main thread.
    /// </summary>
    /// <param name="action">The work to enqueue.</param>
    void Run(Action action);

    /// <summary>
    /// Executes all queued work on the main thread.
    /// </summary>
    void Drain();
}
