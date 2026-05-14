using Emberglass.API.Shared;
using System;
using System.Collections;
using Unity.Entities;
using UnityEngine;

namespace Emberglass.Network.Examples;
/// <summary>
/// Represents a ping request containing client ticks.
/// </summary>
internal readonly struct Ping(long ticks)
{
    public readonly long ClientTicks = ticks;
}
/// <summary>
/// Represents a pong response containing client and server ticks.
/// </summary>
internal readonly struct Pong(long cTicks, long sTicks)
{
    public readonly long ClientTicks = cTicks;
    public readonly long ServerTicks = sTicks;
}
internal static class NetworkTesting
{
    /// <summary>
    /// Demonstrates request/response messaging with a ping/pong exchange.
    /// </summary>
    public static void PingPong()
    {
        if (VWorld.IsClient)
        {
            DelayedPing().Run();
        }
        else if (VWorld.IsServer)
        {
            RegisterPingHandler();
        }
    }

    const float DELAY = 60f;
    const int REQUEST_TIMEOUT_SECONDS = 10;
    static readonly WaitForSeconds _delay = new(DELAY);
    public static bool _ready = false;
    /// <summary>
    /// Waits for readiness before sending the first ping request.
    /// </summary>
    /// <returns>An enumerator for the coroutine.</returns>
    static IEnumerator DelayedPing()
    {
        while (!_ready)
        {
            yield return null;
        }

        yield return _delay;
        SendPingOnce();
    }

    /// <summary>
    /// Registers a request handler that responds to pings with pong payloads.
    /// </summary>
    static void RegisterPingHandler()
    {
        VWorld.Log.LogWarning("[PingPong.Server] Registering -> RequestResponse(Ping/Pong)");
        API.Shared.VNetwork.RegisterRequestHandler<Ping, Pong>((sender, ping) =>
        {
            VWorld.Log.LogWarning($"[ClientPacketReceived] Received ping from {sender.PlatformId}");
            return new Pong(ping.ClientTicks, DateTime.UtcNow.Ticks);
        });
    }

    /// <summary>
    /// Sends a single ping request and logs the round trip time.
    /// </summary>
    static void SendPingOnce()
    {
        long startTicks = DateTime.UtcNow.Ticks;
        try
        {
            API.Shared.VNetwork.SendRequest<Ping, Pong>(
                VWorld.LocalUser.GetUser(),
                new Ping(startTicks),
                TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS),
                pong =>
                {
                    long rttTicks = DateTime.UtcNow.Ticks - pong.ClientTicks;
                    double ms = TimeSpan.FromTicks(rttTicks).TotalMilliseconds;
                    double serverMs = TimeSpan.FromTicks(pong.ServerTicks - pong.ClientTicks).TotalMilliseconds;
                    VWorld.Log.LogWarning($"[ServerPacketReceived] RTT ≈ {ms:F1} ms (server responded in {serverMs:F1} ms)");
                },
                exception =>
                {
                    if (exception is TimeoutException)
                    {
                        VWorld.Log.LogWarning("[PingPong.Client] Ping request timed out.");
                        return;
                    }

                    VWorld.Log.LogWarning($"[PingPong.Client] Ping request failed: {exception.Message}");
                });
        }
        catch (Exception ex)
        {
            VWorld.Log.LogWarning($"[PingPong.Client] Ping request failed: {ex.Message}");
        }
    }
}
