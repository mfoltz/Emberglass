using System.Collections;
using System.Reflection;
using Emberglass.API.Shared;
using Emberglass.Network;
using ProjectM.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for request/response lifecycle cleanup.
/// </summary>
[Collection("Assembly setup")]
public sealed class RequestResponseTests : IDisposable
{
    readonly bool originalIsReady;

    /// <summary>
    /// Initializes the test and records mutable network state.
    /// </summary>
    public RequestResponseTests()
    {
        originalIsReady = VNetwork.IsReady;
    }

    /// <summary>
    /// Ensures an initial send failure removes the pending request immediately.
    /// </summary>
    [Fact]
    public async Task SendRequestAsync_RemovesPendingRequestWhenInitialSendFails()
    {
        const ulong PlatformId = 12001;
        User Target = new();

        using IDisposable runtimeContextScope = VWorld.BeginRuntimeContextOverride(isClient: false);
        using IDisposable platformIdScope = PacketRelay.BeginPlatformIdOverride(user => PlatformId);
        using IDisposable targetIdScope = RequestResponse.BeginTargetIdOverride(user => PlatformId);
        using PendingRequestScope pendingRequestScope = new();

        SetVNetworkReady(true);

        InvalidOperationException Exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await VNetwork.SendRequestAsync<RequestPacket, ResponsePacket>(
                Target,
                new RequestPacket(1),
                TimeSpan.FromSeconds(30)));

        Assert.Contains("SendPacketFromServer cannot send before the target client session is ready", Exception.Message);
        Assert.Equal(0, pendingRequestScope.Count);
    }

    /// <summary>
    /// Ensures client pending requests are faulted when the local client session resets.
    /// </summary>
    [Fact]
    public async Task MarkClientSessionNotReady_FaultsPendingClientRequests()
    {
        using IDisposable runtimeContextScope = VWorld.BeginRuntimeContextOverride(isClient: true);
        using PendingRequestScope pendingRequestScope = new();

        Task<ResponsePacket> PendingTask = pendingRequestScope.AddPendingRequest<ResponsePacket>(
            requestId: 42,
            targetId: 0,
            timeout: TimeSpan.FromSeconds(30));

        SetVNetworkReady(true);

        VNetwork.MarkClientSessionNotReady();

        InvalidOperationException Exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await PendingTask);

        Assert.Contains("client session disconnected or reset", Exception.Message);
        Assert.Equal(0, pendingRequestScope.Count);
        Assert.False(VNetwork.IsReady);
    }

    /// <summary>
    /// Characterizes fallback task behavior when no main-thread invoker is available.
    /// </summary>
    [Fact]
    public async Task PendingRequestContinuation_WithoutMainThreadInvoker_CompletesOnCallerThread()
    {
        using PendingRequestScope pendingRequestScope = new();

        int callerThreadId = Environment.CurrentManagedThreadId;
        Task<ResponsePacket> PendingTask = pendingRequestScope.AddPendingRequest<ResponsePacket>(
            requestId: 77,
            targetId: 0,
            timeout: TimeSpan.FromSeconds(30));

        SynchronizationContext? originalContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            Task<int> ContinuationThreadTask = CaptureContinuationThreadAsync(PendingTask);

            pendingRequestScope.CompletePendingRequest(77, new ResponsePacket());

            int continuationThreadId = await ContinuationThreadTask;

            Assert.Equal(callerThreadId, continuationThreadId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    /// <summary>
    /// Ensures compatibility task completions are marshaled through the main-thread invoker.
    /// </summary>
    [Fact]
    public async Task PendingRequestCompletion_WithMainThreadInvoker_CompletesTaskOnInvokerDrain()
    {
        using PendingRequestScope pendingRequestScope = new();
        var invoker = new QueueingMainThreadInvoker(isMainThread: false);
        using IDisposable invokerScope = new MainThreadInvokerScope(invoker);

        Task<ResponsePacket> PendingTask = pendingRequestScope.AddPendingRequest<ResponsePacket>(
            requestId: 80,
            targetId: 0,
            timeout: TimeSpan.FromSeconds(30));
        Task<int> ContinuationThreadTask = CaptureContinuationThreadAsync(PendingTask);

        pendingRequestScope.CompletePendingRequest(80, new ResponsePacket());

        Assert.False(PendingTask.IsCompleted);
        await invoker.WaitForQueuedActionAsync();

        invoker.Drain();

        Assert.True(PendingTask.IsCompletedSuccessfully);
        Assert.Equal(invoker.LastDrainThreadId, await ContinuationThreadTask);
    }

    /// <summary>
    /// Ensures request callbacks are routed through the main-thread invoker.
    /// </summary>
    [Fact]
    public async Task CompleteOnMainThread_QueuesResponseCallbackOnMainThreadInvoker()
    {
        using PendingRequestScope pendingRequestScope = new();
        var invoker = new QueueingMainThreadInvoker();
        ResponsePacket? ReceivedResponse = null;

        Task<ResponsePacket> PendingTask = pendingRequestScope.AddPendingRequest<ResponsePacket>(
            requestId: 78,
            targetId: 0,
            timeout: TimeSpan.FromSeconds(30));

        RequestResponse.CompleteOnMainThread(
            PendingTask,
            invoker,
            response => ReceivedResponse = response,
            _ => throw new InvalidOperationException("Unexpected request failure."));

        pendingRequestScope.CompletePendingRequest(78, new ResponsePacket { Value = 12 });
        await invoker.WaitForQueuedActionAsync();

        Assert.Null(ReceivedResponse);

        invoker.Drain();

        Assert.NotNull(ReceivedResponse);
        Assert.Equal(12, ReceivedResponse.Value);
    }

    /// <summary>
    /// Ensures request failures are routed through the main-thread invoker.
    /// </summary>
    [Fact]
    public async Task CompleteOnMainThread_QueuesFailureCallbackOnMainThreadInvoker()
    {
        using PendingRequestScope pendingRequestScope = new();
        var invoker = new QueueingMainThreadInvoker();
        Exception? ReceivedException = null;

        Task<ResponsePacket> PendingTask = pendingRequestScope.AddPendingRequest<ResponsePacket>(
            requestId: 79,
            targetId: 0,
            timeout: TimeSpan.FromSeconds(30));

        RequestResponse.CompleteOnMainThread(
            PendingTask,
            invoker,
            _ => throw new InvalidOperationException("Unexpected request success."),
            exception => ReceivedException = exception);

        pendingRequestScope.FaultPendingRequest(79, new InvalidOperationException("request failed"));
        await invoker.WaitForQueuedActionAsync();

        Assert.Null(ReceivedException);

        invoker.Drain();

        Assert.NotNull(ReceivedException);
        Assert.Contains("request failed", ReceivedException.Message);
    }

    /// <summary>
    /// Ensures initial send failures are routed through the error callback when one is provided.
    /// </summary>
    [Fact]
    public async Task SendRequest_WhenInitialSendFails_QueuesFailureCallbackOnMainThreadInvoker()
    {
        const ulong PlatformId = 12002;
        User Target = new();
        using IDisposable runtimeContextScope = VWorld.BeginRuntimeContextOverride(isClient: false);
        using IDisposable platformIdScope = PacketRelay.BeginPlatformIdOverride(user => PlatformId);
        using IDisposable targetIdScope = RequestResponse.BeginTargetIdOverride(user => PlatformId);
        using PendingRequestScope pendingRequestScope = new();
        var invoker = new QueueingMainThreadInvoker();
        using IDisposable invokerScope = new MainThreadInvokerScope(invoker);
        Exception? ReceivedException = null;

        SetVNetworkReady(true);

        RequestResponse.SendRequest<RequestPacket, ResponsePacket>(
            Target,
            new RequestPacket(1),
            TimeSpan.FromSeconds(30),
            _ => throw new InvalidOperationException("Unexpected request success."),
            exception => ReceivedException = exception);
        await invoker.WaitForQueuedActionAsync();

        Assert.Null(ReceivedException);

        invoker.Drain();

        Assert.NotNull(ReceivedException);
        Assert.Contains("SendPacketFromServer cannot send before the target client session is ready", ReceivedException.Message);
        Assert.Equal(0, pendingRequestScope.Count);
    }

    /// <summary>
    /// Restores mutable network state.
    /// </summary>
    public void Dispose()
        => SetVNetworkReady(originalIsReady);

    static void SetVNetworkReady(bool isReady)
    {
        PropertyInfo Property = typeof(VNetwork).GetProperty(
            nameof(VNetwork.IsReady),
            BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("VNetwork.IsReady property not found.");
        Property.SetValue(null, isReady);
    }

    static async Task<int> CaptureContinuationThreadAsync(Task<ResponsePacket> pendingTask)
    {
        await pendingTask;
        return Environment.CurrentManagedThreadId;
    }

    sealed class RequestPacket
    {
        public int Value { get; set; }

        public RequestPacket(int value)
            => Value = value;
    }

    sealed class ResponsePacket
    {
        public int Value { get; set; }
    }

    sealed class PendingRequestScope : IDisposable
    {
        readonly IDictionary pendingRequests;
        readonly Dictionary<object, object> originalEntries;
        readonly Type requestResponseType;

        public PendingRequestScope()
        {
            requestResponseType = typeof(Registry).Assembly.GetType("Emberglass.Network.RequestResponse")
                ?? throw new InvalidOperationException("RequestResponse type not found.");
            FieldInfo PendingRequestsField = requestResponseType.GetField(
                "_pendingRequests",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("_pendingRequests field not found.");
            pendingRequests = (IDictionary)(PendingRequestsField.GetValue(null)
                ?? throw new InvalidOperationException("_pendingRequests field value missing."));

            originalEntries = new Dictionary<object, object>();
            foreach (DictionaryEntry entry in pendingRequests)
            {
                if (entry.Value is not null)
                {
                    originalEntries[entry.Key] = entry.Value;
                }
            }

            pendingRequests.Clear();
        }

        public int Count => pendingRequests.Count;

        public Task<TResponse> AddPendingRequest<TResponse>(long requestId, ulong targetId, TimeSpan timeout)
        {
            MethodInfo CreateMethod = requestResponseType.GetMethod(
                "CreatePendingRequest",
                BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("CreatePendingRequest method not found.");
            MethodInfo GenericMethod = CreateMethod.MakeGenericMethod(typeof(TResponse));
            object Pending = GenericMethod.Invoke(null, new object[] { requestId, targetId, timeout })
                ?? throw new InvalidOperationException("CreatePendingRequest returned null.");
            pendingRequests[requestId] = Pending;

            PropertyInfo TaskProperty = Pending.GetType().GetProperty(
                "Task",
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("Pending request Task property not found.");

            return (Task<TResponse>)(TaskProperty.GetValue(Pending)
                ?? throw new InvalidOperationException("Pending request Task value missing."));
        }

        public void CompletePendingRequest<TResponse>(long requestId, TResponse response)
        {
            object Pending = pendingRequests[requestId]
                ?? throw new InvalidOperationException($"Pending request {requestId} not found.");
            MethodInfo TrySetResultMethod = Pending.GetType().GetMethod(
                "TrySetResult",
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("Pending request TrySetResult method not found.");

            TrySetResultMethod.Invoke(Pending, new object?[] { response });
        }

        public void FaultPendingRequest(long requestId, Exception exception)
        {
            object Pending = pendingRequests[requestId]
                ?? throw new InvalidOperationException($"Pending request {requestId} not found.");
            MethodInfo TrySetExceptionMethod = Pending.GetType().GetMethod(
                "TrySetException",
                BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("Pending request TrySetException method not found.");

            TrySetExceptionMethod.Invoke(Pending, new object[] { exception });
        }

        public void Dispose()
        {
            pendingRequests.Clear();
            foreach (var entry in originalEntries)
            {
                pendingRequests[entry.Key] = entry.Value;
            }
        }
    }

    sealed class QueueingMainThreadInvoker : IMainThreadInvoker
    {
        readonly Queue<Action> queuedActions = new();
        readonly TaskCompletionSource<object?> queuedActionSource = new();
        readonly bool isMainThread;

        public QueueingMainThreadInvoker(bool isMainThread = true)
        {
            this.isMainThread = isMainThread;
        }

        public bool IsMainThread => isMainThread;

        public int LastDrainThreadId { get; private set; }

        public void Run(Action action)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            queuedActions.Enqueue(action);
            queuedActionSource.TrySetResult(null);
        }

        public void Drain()
        {
            LastDrainThreadId = Environment.CurrentManagedThreadId;
            while (queuedActions.TryDequeue(out var action))
            {
                action();
            }
        }

        public Task WaitForQueuedActionAsync()
            => queuedActionSource.Task;
    }

    sealed class MainThreadInvokerScope : IDisposable
    {
        readonly IMainThreadInvoker originalInvoker;

        public MainThreadInvokerScope(IMainThreadInvoker invoker)
        {
            originalInvoker = VBehaviour.MainThreadInvoker;
            VBehaviour.MainThreadInvoker = invoker;
        }

        public void Dispose()
            => VBehaviour.MainThreadInvoker = originalInvoker;
    }
}
