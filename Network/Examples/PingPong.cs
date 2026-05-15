using Emberglass.API.Shared;
using ProjectM.Network;
using System;
using System.Collections.Generic;

namespace Emberglass.Network.Examples;

/// <summary>
/// Compile-oriented examples that show how to compose VNetwork primitives.
/// </summary>
internal static class NetworkingPrimitiveExamples
{
    const string FEATURE_NAME = "ExampleClientFeature";
    const string FEATURE_VERSION = "1.0.0";
    const string SETTING_KEY = "example.enabled";
    const int REQUEST_TIMEOUT_SECONDS = 10;

    static readonly Dictionary<ulong, ClientFeatureRegistration> registeredFeatures = [];
    static bool clientReadySubscribed;
    static bool localOptimisticValue;
    static bool localAuthoritativeValue;

    /// <summary>
    /// Registers packet handlers and readiness callbacks for the current runtime.
    /// </summary>
    public static void Initialize()
    {
        if (VWorld.IsServer)
        {
            RegisterServerHandlers();
        }

        if (VWorld.IsClient)
        {
            RegisterClientHandlers();

            if (!clientReadySubscribed)
            {
                VNetwork.OnClientReady += RegisterClientFeatureWhenReady;
                clientReadySubscribed = true;
            }

            if (VNetwork.IsReady)
            {
                RegisterClientFeatureWhenReady();
            }
        }
    }

    /// <summary>
    /// Demonstrates a client-originated request with rollback to the authoritative value.
    /// </summary>
    /// <param name="requestedValue">The optimistic setting value requested by the client.</param>
    public static void RequestServerSettingChange(bool requestedValue)
    {
        if (!VWorld.IsClient || !VNetwork.IsReady)
        {
            RestoreAuthoritativeValue();
            return;
        }

        localOptimisticValue = requestedValue;

        VNetwork.SendRequest<ServerSettingChangeRequest, ServerSettingChangeReceipt>(
            VWorld.LocalUser.GetUser(),
            new ServerSettingChangeRequest(SETTING_KEY, requestedValue),
            TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS),
            ApplyServerReceipt,
            exception =>
            {
                VWorld.Log.LogWarning($"[NetworkingExamples] setting change failed: {exception.Message}");
                RestoreAuthoritativeValue();
            });
    }

    /// <summary>
    /// Demonstrates a soft bridge decision point without owning the legacy transport.
    /// </summary>
    /// <param name="sendLegacy">Compatibility sender used when Emberglass is unavailable.</param>
    public static void SendWithLegacyFallback(Action sendLegacy)
    {
        ArgumentNullException.ThrowIfNull(sendLegacy);

        if (!VWorld.IsClient || !VNetwork.IsReady)
        {
            sendLegacy();
            return;
        }

        VNetwork.SendToServer(new ClientTypedSignal("sent through Emberglass"));
    }

    static void RegisterServerHandlers()
    {
        VNetwork.RegisterServerbound<ClientTypedSignal>((sender, packet) =>
            VWorld.Log.LogInfo($"[NetworkingExamples] client signal from {sender.PlatformId}: {packet.Message}"));

        VNetwork.RegisterServerbound<ClientFeatureRegistration>((sender, packet) =>
        {
            registeredFeatures[sender.PlatformId] = packet;
            VWorld.Log.LogInfo(
                $"[NetworkingExamples] registered {packet.FeatureName} {packet.FeatureVersion} for {sender.PlatformId}");
            VNetwork.SendToClient(sender, new ServerFeatureState(packet.FeatureName, progress: 1, status: "registered"));
        });

        VNetwork.RegisterRequestHandler<ServerSettingChangeRequest, ServerSettingChangeReceipt>(
            (_, request) => ValidateServerSettingChange(request));
    }

    static void RegisterClientHandlers()
    {
        VNetwork.RegisterClientbound<ServerTypedSignal>((_, packet) =>
            VWorld.Log.LogInfo($"[NetworkingExamples] server signal: {packet.Message}"));

        VNetwork.RegisterClientbound<ServerFeatureState>((_, packet) =>
            VWorld.Log.LogInfo($"[NetworkingExamples] {packet.FeatureName} state: {packet.Status} ({packet.Progress})"));
    }

    static void RegisterClientFeatureWhenReady()
    {
        VNetwork.SendToServer(new ClientFeatureRegistration(FEATURE_NAME, FEATURE_VERSION));
    }

    static ServerSettingChangeReceipt ValidateServerSettingChange(ServerSettingChangeRequest request)
    {
        bool accepted = string.Equals(request.RequestKey, SETTING_KEY, StringComparison.Ordinal);
        bool authoritativeValue = accepted && request.RequestedValue;
        string rejectionReason = accepted ? string.Empty : "Unknown setting.";

        return new ServerSettingChangeReceipt(
            request.RequestKey,
            authoritativeValue,
            accepted,
            rejectionReason);
    }

    static void ApplyServerReceipt(ServerSettingChangeReceipt receipt)
    {
        if (!receipt.IsAccepted && !string.IsNullOrWhiteSpace(receipt.RejectionReason))
        {
            VWorld.Log.LogWarning($"[NetworkingExamples] setting rejected: {receipt.RejectionReason}");
        }

        localAuthoritativeValue = receipt.AuthoritativeValue;
        localOptimisticValue = receipt.AuthoritativeValue;
    }

    static void RestoreAuthoritativeValue()
    {
        localOptimisticValue = localAuthoritativeValue;
    }
}
