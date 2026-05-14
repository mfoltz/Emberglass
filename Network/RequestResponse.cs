using Emberglass.API.Shared;
using ProjectM.Network;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using static Emberglass.API.Server.ServerModules.ConnectionModules;
using static Emberglass.Network.Registry;

namespace Emberglass.Network;
/// <summary>
/// Provides a lightweight request/response messaging layer built on VNetwork packets.
/// </summary>
internal static class RequestResponse
{
    /// <summary>
    /// Wraps a request payload with identifiers for request/response correlation.
    /// </summary>
    /// <typeparam name="TPayload">The request payload type.</typeparam>
    internal readonly struct RequestEnvelope<TPayload>
    {
        public readonly long RequestId;
        public readonly long? ResponseTo;
        public readonly TPayload Payload;

        /// <summary>
        /// Initializes a new request envelope.
        /// </summary>
        /// <param name="requestId">The unique request identifier.</param>
        /// <param name="responseTo">The request identifier this envelope responds to, if any.</param>
        /// <param name="payload">The request payload.</param>
        public RequestEnvelope(long requestId, long? responseTo, TPayload payload)
        {
            RequestId = requestId;
            ResponseTo = responseTo;
            Payload = payload;
        }
    }

    /// <summary>
    /// Wraps a response payload with identifiers for request/response correlation.
    /// </summary>
    /// <typeparam name="TPayload">The response payload type.</typeparam>
    internal readonly struct ResponseEnvelope<TPayload>
    {
        public readonly long RequestId;
        public readonly long? ResponseTo;
        public readonly TPayload Payload;

        /// <summary>
        /// Initializes a new response envelope.
        /// </summary>
        /// <param name="requestId">The unique response identifier.</param>
        /// <param name="responseTo">The request identifier this response is linked to.</param>
        /// <param name="payload">The response payload.</param>
        public ResponseEnvelope(long requestId, long? responseTo, TPayload payload)
        {
            RequestId = requestId;
            ResponseTo = responseTo;
            Payload = payload;
        }
    }

    /// <summary>
    /// Defines operations for a pending request.
    /// </summary>
    interface IPendingRequest
    {
        /// <summary>
        /// Gets the identifier of the user associated with the request.
        /// </summary>
        ulong TargetId { get; }

        /// <summary>
        /// Completes the pending request with an exception.
        /// </summary>
        /// <param name="exception">The exception to propagate.</param>
        void TrySetException(Exception exception);
    }

    /// <summary>
    /// Tracks a pending request awaiting a typed response payload.
    /// </summary>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    sealed class PendingRequest<TResponse> : IPendingRequest
    {
        readonly TaskCompletionSource<TResponse> completionSource;
        readonly CancellationTokenSource timeoutSource;
        readonly CancellationTokenRegistration timeoutRegistration;
        readonly ulong targetId;

        /// <summary>
        /// Initializes a new pending request with a timeout.
        /// </summary>
        /// <param name="timeout">The request timeout duration.</param>
        /// <param name="onTimeout">The callback invoked on timeout.</param>
        public PendingRequest(ulong targetId, TimeSpan timeout, Action onTimeout)
        {
            this.targetId = targetId;
            completionSource = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            timeoutSource = new CancellationTokenSource(timeout);
            timeoutRegistration = timeoutSource.Token.Register(onTimeout);
        }

        /// <summary>
        /// Gets the identifier of the user associated with the request.
        /// </summary>
        public ulong TargetId => targetId;

        /// <summary>
        /// Gets the response task.
        /// </summary>
        public Task<TResponse> Task => completionSource.Task;

        /// <summary>
        /// Attempts to complete the request with a response payload.
        /// </summary>
        /// <param name="response">The response payload.</param>
        /// <returns>True when the response was applied.</returns>
        public bool TrySetResult(TResponse response)
        {
            if (!completionSource.TrySetResult(response))
            {
                return false;
            }

            Dispose();
            return true;
        }

        /// <summary>
        /// Attempts to complete the request with an exception.
        /// </summary>
        /// <param name="exception">The exception to propagate.</param>
        public void TrySetException(Exception exception)
        {
            if (completionSource.TrySetException(exception))
            {
                Dispose();
            }
        }

        /// <summary>
        /// Releases timeout resources for the pending request.
        /// </summary>
        void Dispose()
        {
            timeoutRegistration.Dispose();
            timeoutSource.Dispose();
        }
    }

    static readonly ConcurrentDictionary<long, IPendingRequest> _pendingRequests = new();
    static readonly ConcurrentDictionary<Type, Direction> _responseHandlerDirections = new();
    static long _nextRequestId;
    static bool _initialized;

    /// <summary>
    /// Sends a typed request and awaits the typed response payload, canceling when the target disconnects
    /// or when the plugin shuts down.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type.</typeparam>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    /// <param name="target">The remote user to receive the request when acting as a server.</param>
    /// <param name="request">The request payload to send.</param>
    /// <param name="timeout">The amount of time to wait for a response.</param>
    /// <returns>A task that completes with the response payload.</returns>
    public static Task<TResponse> SendRequestAsync<TRequest, TResponse>(User target, TRequest request, TimeSpan timeout)
    {
        ValidateTimeout(timeout);
        long requestId = NextRequestId();
        ulong targetId = GetTargetId(target);
        var pending = CreatePendingRequest<TResponse>(requestId, targetId, timeout);

        if (!_pendingRequests.TryAdd(requestId, pending))
        {
            throw new InvalidOperationException($"Failed to track pending request {requestId}.");
        }

        Direction responseDirection = GetResponseDirectionForCurrentSide();
        EnsureResponseHandlerRegistered<TResponse>(responseDirection);
        SendRequestEnvelope(target, new RequestEnvelope<TRequest>(requestId, null, request));
        return pending.Task;
    }

    /// <summary>
    /// Registers a handler that responds to typed requests with typed responses.
    /// Pending requests are faulted if the remote user disconnects or the plugin shuts down.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type.</typeparam>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    /// <param name="handler">The handler invoked when a request is received.</param>
    public static void RegisterRequestHandler<TRequest, TResponse>(Func<User, TRequest, TResponse> handler)
    {
        Direction requestDirection = GetRequestDirectionForCurrentSide();
        Register<RequestEnvelope<TRequest>>(requestDirection, (sender, obj) =>
            HandleRequestEnvelope(sender, (RequestEnvelope<TRequest>)obj, handler, requestDirection));
    }

    /// <summary>
    /// Executes a request handler and sends the typed response envelope.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type.</typeparam>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    /// <param name="sender">The user who sent the request.</param>
    /// <param name="envelope">The incoming request envelope.</param>
    /// <param name="handler">The handler used to generate a response.</param>
    /// <param name="requestDirection">The direction the request traveled.</param>
    static void HandleRequestEnvelope<TRequest, TResponse>(
        User sender,
        RequestEnvelope<TRequest> envelope,
        Func<User, TRequest, TResponse> handler,
        Direction requestDirection)
    {
        TResponse responsePayload = handler(sender, envelope.Payload);
        ResponseEnvelope<TResponse> responseEnvelope = new(NextRequestId(), envelope.RequestId, responsePayload);
        SendResponseEnvelope(sender, requestDirection, responseEnvelope);
    }

    /// <summary>
    /// Creates a pending request that will be cancelled when the timeout elapses.
    /// </summary>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>A pending request instance.</returns>
    static PendingRequest<TResponse> CreatePendingRequest<TResponse>(long requestId, ulong targetId, TimeSpan timeout)
    {
        return new PendingRequest<TResponse>(
            targetId,
            timeout,
            () => OnRequestTimeout(requestId, timeout));
    }

    /// <summary>
    /// Cancels a pending request when the timeout elapses.
    /// </summary>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="timeout">The timeout duration.</param>
    static void OnRequestTimeout(long requestId, TimeSpan timeout)
    {
        if (!_pendingRequests.TryRemove(requestId, out var pending))
        {
            return;
        }

        pending.TrySetException(new TimeoutException($"Request {requestId} timed out after {timeout}."));
    }

    /// <summary>
    /// Registers a response handler for the typed response envelope.
    /// </summary>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    /// <param name="responseDirection">The direction responses will travel.</param>
    static void EnsureResponseHandlerRegistered<TResponse>(Direction responseDirection)
    {
        Type responseType = typeof(ResponseEnvelope<TResponse>);
        if (_responseHandlerDirections.TryGetValue(responseType, out Direction existingDirection))
        {
            if (existingDirection != responseDirection)
            {
                throw new InvalidOperationException(
                    $"Response handler for {responseType.Name} already registered with {existingDirection}.");
            }

            return;
        }

        _responseHandlerDirections[responseType] = responseDirection;
        Register<ResponseEnvelope<TResponse>>(responseDirection, (sender, obj) =>
            HandleResponseEnvelope((ResponseEnvelope<TResponse>)obj));
    }

    /// <summary>
    /// Handles a response envelope and completes the associated pending request.
    /// </summary>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    /// <param name="envelope">The response envelope.</param>
    static void HandleResponseEnvelope<TResponse>(ResponseEnvelope<TResponse> envelope)
    {
        if (!envelope.ResponseTo.HasValue)
        {
            return;
        }

        long requestId = envelope.ResponseTo.Value;
        if (!_pendingRequests.TryRemove(requestId, out var pending))
        {
            return;
        }

        if (pending is not PendingRequest<TResponse> typedPending)
        {
            pending.TrySetException(new InvalidOperationException(
                $"Response payload type mismatch for request {requestId}."));
            return;
        }

        typedPending.TrySetResult(envelope.Payload);
    }

    /// <summary>
    /// Sends a request envelope to the appropriate remote endpoint.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type.</typeparam>
    /// <param name="target">The target user when sending from the server.</param>
    /// <param name="envelope">The envelope to send.</param>
    static void SendRequestEnvelope<TRequest>(User target, RequestEnvelope<TRequest> envelope)
    {
        if (VWorld.IsServer)
        {
            PacketRelay.SendPacketFromServer(target, envelope);
            return;
        }

        if (VWorld.IsClient)
        {
            PacketRelay.SendPacketFromClient(VWorld.LocalUser.GetUser(), envelope);
            return;
        }

        throw new InvalidOperationException("Request/response messaging requires a client or server context.");
    }

    /// <summary>
    /// Sends a response envelope back to the requester.
    /// </summary>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    /// <param name="sender">The user associated with the request.</param>
    /// <param name="requestDirection">The direction the request traveled.</param>
    /// <param name="envelope">The response envelope to send.</param>
    static void SendResponseEnvelope<TResponse>(User sender, Direction requestDirection, ResponseEnvelope<TResponse> envelope)
    {
        if (requestDirection == Direction.Serverbound)
        {
            PacketRelay.SendPacketFromServer(sender, envelope);
            return;
        }

        PacketRelay.SendPacketFromClient(VWorld.LocalUser.GetUser(), envelope);
    }

    /// <summary>
    /// Determines the request direction for the current runtime side.
    /// </summary>
    /// <returns>The expected request direction.</returns>
    static Direction GetRequestDirectionForCurrentSide()
    {
        if (VWorld.IsServer)
        {
            return Direction.Serverbound;
        }

        if (VWorld.IsClient)
        {
            return Direction.Clientbound;
        }

        throw new InvalidOperationException("Request/response handlers require a client or server context.");
    }

    /// <summary>
    /// Determines the response direction for the current runtime side.
    /// </summary>
    /// <returns>The expected response direction.</returns>
    static Direction GetResponseDirectionForCurrentSide()
    {
        if (VWorld.IsServer)
        {
            return Direction.Serverbound;
        }

        if (VWorld.IsClient)
        {
            return Direction.Clientbound;
        }

        throw new InvalidOperationException("Request/response handlers require a client or server context.");
    }

    /// <summary>
    /// Generates the next request identifier.
    /// </summary>
    /// <returns>A monotonically increasing request identifier.</returns>
    static long NextRequestId() => Interlocked.Increment(ref _nextRequestId);

    /// <summary>
    /// Initializes request/response lifecycle tracking.
    /// </summary>
    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        if (VWorld.IsServer)
        {
            VEvents.ModuleRegistry.Subscribe<UserDisconnected>(OnUserDisconnected);
        }

        _initialized = true;
    }

    /// <summary>
    /// Clears pending requests and faults outstanding tasks during shutdown.
    /// </summary>
    internal static void Uninitialize()
    {
        if (!_initialized)
        {
            return;
        }

        FaultAndClearPendingRequests("Request/response messaging is shutting down.");
        _responseHandlerDirections.Clear();
        _initialized = false;
    }

    /// <summary>
    /// Validates the timeout value for requests.
    /// </summary>
    /// <param name="timeout">The timeout duration.</param>
    static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be greater than zero.");
        }
    }

    static ulong GetTargetId(User target) => target.PlatformId;

    static void OnUserDisconnected(UserDisconnected userDisconnected)
    {
        ulong targetId = userDisconnected.PlayerInfo.SteamId;
        FaultPendingRequestsForTarget(targetId, requestId =>
            new InvalidOperationException($"Request {requestId} canceled because the user disconnected."));
    }

    static void FaultAndClearPendingRequests(string reason)
    {
        FaultPendingRequestsForTarget(null, requestId => new InvalidOperationException(
            $"Request {requestId} canceled because {reason}"));
    }

    static void FaultPendingRequestsForTarget(ulong? targetId, Func<long, Exception> exceptionFactory)
    {
        foreach (var pendingEntry in _pendingRequests)
        {
            if (targetId.HasValue && pendingEntry.Value.TargetId != targetId.Value)
            {
                continue;
            }

            if (_pendingRequests.TryRemove(pendingEntry.Key, out var pending))
            {
                pending.TrySetException(exceptionFactory(pendingEntry.Key));
            }
        }
    }
}
