using Emberglass.API.Server;
using Emberglass.Network;
using ProjectM.Network;
using static Emberglass.Network.Registry;

namespace Emberglass.API.Shared;
public static class VNetwork
{
    /// <summary>
    /// Gets a value indicating whether the network stack has completed initialization.
    /// </summary>
    /// <remarks>
    /// This becomes <see langword="true" /> once <see cref="Initialize" /> completes.
    /// Use <see cref="OnReady" /> or <see cref="OnClientReady" /> to wait for readiness.
    /// </remarks>
    public static bool IsReady { get; private set; }

    /// <summary>
    /// Raised on the server when a client completes the network handshake and is ready for packets.
    /// Subscribe to this event to perform server-side initialization for the connected user after
    /// the secure session is established.
    /// </summary>
    public static event Action<User> OnReady;

    /// <summary>
    /// Raised on the client when the handshake with the server completes and packets can be sent.
    /// Subscribe to this event to perform client-side initialization that depends on the secure
    /// session being established.
    /// </summary>
    public static event Action OnClientReady;

    /// <summary>
    /// Registers a serverbound packet handler for blittable packet types.
    /// </summary>
    /// <param name="handler">The handler invoked when the packet is received.</param>
    /// <typeparam name="T">The unmanaged packet type.</typeparam>
    /// <remarks>
    /// Unmanaged packets are serialized as a fixed-size binary payload using <c>Marshal.SizeOf</c>.
    /// </remarks>
    public static void RegisterServerboundStruct<T>(Action<User, T> handler) where T : unmanaged
        => RegisterServerboundInternal(handler);

    /// <summary>
    /// Registers a clientbound packet handler for blittable packet types.
    /// </summary>
    /// <param name="handler">The handler invoked when the packet is received.</param>
    /// <typeparam name="T">The unmanaged packet type.</typeparam>
    /// <remarks>
    /// Unmanaged packets are serialized as a fixed-size binary payload using <c>Marshal.SizeOf</c>.
    /// </remarks>
    public static void RegisterClientboundStruct<T>(Action<User, T> handler) where T : unmanaged
        => RegisterClientboundInternal(handler);

    /// <summary>
    /// Registers both serverbound and clientbound handlers for a blittable packet type.
    /// </summary>
    /// <param name="serverHandler">The handler invoked for serverbound packets.</param>
    /// <param name="clientHandler">The handler invoked for clientbound packets.</param>
    /// <typeparam name="T">The unmanaged packet type.</typeparam>
    /// <remarks>
    /// Unmanaged packets are serialized as a fixed-size binary payload using <c>Marshal.SizeOf</c>.
    /// </remarks>
    public static void RegisterBiDirectionalStruct<T>(
        Action<User, T> serverHandler,
        Action<User, T> clientHandler) where T : unmanaged
    {
        RegisterServerboundStruct(serverHandler);
        RegisterClientboundStruct(clientHandler);
    }

    /// <summary>
    /// Registers a serverbound packet handler for any packet type.
    /// </summary>
    /// <param name="handler">The handler invoked when the packet is received.</param>
    /// <typeparam name="T">The packet type.</typeparam>
    /// <remarks>
    /// Non-blittable packets are serialized using the JSON settings in <c>Network/Serialization.cs</c>.
    /// JSON payloads are variable in size and may be larger than unmanaged packets, so keep payloads small.
    /// </remarks>
    public static void RegisterServerbound<T>(Action<User, T> handler)
        => RegisterServerboundInternal(handler);

    /// <summary>
    /// Registers a clientbound packet handler for any packet type.
    /// </summary>
    /// <param name="handler">The handler invoked when the packet is received.</param>
    /// <typeparam name="T">The packet type.</typeparam>
    /// <remarks>
    /// Non-blittable packets are serialized using the JSON settings in <c>Network/Serialization.cs</c>.
    /// JSON payloads are variable in size and may be larger than unmanaged packets, so keep payloads small.
    /// </remarks>
    public static void RegisterClientbound<T>(Action<User, T> handler)
        => RegisterClientboundInternal(handler);

    /// <summary>
    /// Unregisters a previously registered packet type.
    /// </summary>
    /// <typeparam name="T">The packet type to unregister.</typeparam>
    public static void Unregister<T>() => Registry.Unregister<T>();

    /// <summary>
    /// Sends a request payload to the target and awaits a typed response payload. Pending requests
    /// are faulted if the target disconnects or the plugin shuts down.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type.</typeparam>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    /// <param name="target">The remote user to receive the request when acting as a server.</param>
    /// <param name="request">The request payload to send.</param>
    /// <param name="timeout">The amount of time to wait for a response.</param>
    /// <returns>A task that completes with the response payload.</returns>
    public static Task<TResponse> SendRequestAsync<TRequest, TResponse>(User target, TRequest request, TimeSpan timeout)
        => RequestResponse.SendRequestAsync<TRequest, TResponse>(target, request, timeout);

    /// <summary>
    /// Registers a handler that responds to typed requests with typed responses. Pending requests
    /// are faulted if the remote user disconnects or the plugin shuts down.
    /// </summary>
    /// <typeparam name="TRequest">The request payload type.</typeparam>
    /// <typeparam name="TResponse">The response payload type.</typeparam>
    /// <param name="handler">The handler invoked when a request is received.</param>
    public static void RegisterRequestHandler<TRequest, TResponse>(Func<User, TRequest, TResponse> handler)
        => RequestResponse.RegisterRequestHandler(handler);

    /// <summary>
    /// Sends a packet to the server from the local client.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <typeparam name="T">The packet type.</typeparam>
    /// <remarks>
    /// Non-blittable packets are serialized using the JSON settings in <c>Network/Serialization.cs</c>.
    /// JSON payloads are variable in size and may be larger than unmanaged packets, so keep payloads small.
    /// This method requires <see cref="IsReady" /> and a client context.
    /// </remarks>
    public static void SendToServer<T>(T packet)
    {
        EnsureReady(nameof(SendToServer));
        EnsureClientContext(nameof(SendToServer));
        PacketRelay.SendPacketFromClient(VWorld.LocalUser.GetUser(), packet);
    }

    /// <summary>
    /// Sends a packet to a client from the server.
    /// </summary>
    /// <param name="target">The user to receive the packet.</param>
    /// <param name="packet">The packet to send.</param>
    /// <typeparam name="T">The packet type.</typeparam>
    /// <remarks>
    /// Non-blittable packets are serialized using the JSON settings in <c>Network/Serialization.cs</c>.
    /// JSON payloads are variable in size and may be larger than unmanaged packets, so keep payloads small.
    /// This method requires <see cref="IsReady" /> and a server context.
    /// </remarks>
    public static void SendToClient<T>(User target, T packet)
    {
        EnsureReady(nameof(SendToClient));
        EnsureServerContext(nameof(SendToClient));
        PacketRelay.SendPacketFromServer(target, packet);
    }

    /// <summary>
    /// Sends a packet to every connected client.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <typeparam name="T">The packet type.</typeparam>
    /// <remarks>
    /// Uses the online player cache to enumerate connected users.
    /// This method requires <see cref="IsReady" /> and a server context.
    /// </remarks>
    public static void SendToAllClients<T>(T packet)
    {
        EnsureReady(nameof(SendToAllClients));
        EnsureServerContext(nameof(SendToAllClients));

        foreach (Players.PlayerInfo playerInfo in Players.SteamIdOnlinePlayerInfoCache.Values)
        {
            SendToClient(playerInfo.User, packet);
        }
    }

    /// <summary>
    /// Sends a packet to multiple client targets.
    /// </summary>
    /// <param name="targets">The users to receive the packet.</param>
    /// <param name="packet">The packet to send.</param>
    /// <typeparam name="T">The packet type.</typeparam>
    public static void SendToClients<T>(IEnumerable<User> targets, T packet)
    {
        if (targets is null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        foreach (User target in targets)
        {
            SendToClient(target, packet);
        }
    }

    /// <summary>
    /// Sends a blittable packet to the server from the local client.
    /// </summary>
    /// <param name="packet">The unmanaged packet to send.</param>
    /// <typeparam name="T">The unmanaged packet type.</typeparam>
    /// <remarks>
    /// Unmanaged packets are serialized as a fixed-size binary payload using <c>Marshal.SizeOf</c>.
    /// This method requires <see cref="IsReady" /> and a client context.
    /// </remarks>
    public static void SendToServerStruct<T>(T packet) where T : unmanaged
    {
        EnsureReady(nameof(SendToServerStruct));
        EnsureClientContext(nameof(SendToServerStruct));
        PacketRelay.SendPacketFromClient(VWorld.LocalUser.GetUser(), packet);
    }

    /// <summary>
    /// Sends a blittable packet to a client from the server.
    /// </summary>
    /// <param name="target">The user to receive the packet.</param>
    /// <param name="packet">The unmanaged packet to send.</param>
    /// <typeparam name="T">The unmanaged packet type.</typeparam>
    /// <remarks>
    /// Unmanaged packets are serialized as a fixed-size binary payload using <c>Marshal.SizeOf</c>.
    /// This method requires <see cref="IsReady" /> and a server context.
    /// </remarks>
    public static void SendToClientStruct<T>(User target, T packet) where T : unmanaged
    {
        EnsureReady(nameof(SendToClientStruct));
        EnsureServerContext(nameof(SendToClientStruct));
        PacketRelay.SendPacketFromServer(target, packet);
    }

    static void RegisterServerboundInternal<T>(Action<User, T> handler)
        => RegisterInternal<T>(Direction.Serverbound, (sender, obj) => handler(sender, (T)obj));

    static void RegisterClientboundInternal<T>(Action<User, T> handler)
        => RegisterInternal<T>(Direction.Clientbound, (sender, obj) => handler(sender, (T)obj));

    static void RegisterInternal<T>(Direction dir, Action<User, object> boxedHandler)
        => Registry.Register<T>(dir, boxedHandler);

    internal static void Initialize()
    {
        Bootstrapper.Awake();
        RequestResponse.Initialize();
        IsReady = true;
    }

    internal static void RaiseServerReady(User user)
        => OnReady?.Invoke(user);

    internal static void RaiseClientReady()
        => OnClientReady?.Invoke();

    static void EnsureReady(string callerName)
    {
        if (IsReady)
        {
            return;
        }

        string message = $"[VNetwork] {callerName} cannot be used before VNetwork.Initialize completes. " +
            "Wait for VNetwork.OnReady or VNetwork.OnClientReady before sending packets.";
        VWorld.Log?.LogError(message);
        throw new InvalidOperationException(message);
    }

    static void EnsureClientContext(string callerName)
    {
        if (VWorld.IsClient)
        {
            return;
        }

        string message = $"[VNetwork] {callerName} is client-only and cannot be used on the server.";
        VWorld.Log?.LogError(message);
        throw new InvalidOperationException(message);
    }

    static void EnsureServerContext(string callerName)
    {
        if (VWorld.IsServer)
        {
            return;
        }

        string message = $"[VNetwork] {callerName} is server-only and cannot be used on the client.";
        VWorld.Log?.LogError(message);
        throw new InvalidOperationException(message);
    }
}
