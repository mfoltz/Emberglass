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

        public PendingRequestScope()
        {
            Type RequestResponseType = typeof(Registry).Assembly.GetType("Emberglass.Network.RequestResponse")
                ?? throw new InvalidOperationException("RequestResponse type not found.");
            FieldInfo PendingRequestsField = RequestResponseType.GetField(
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

        public void Dispose()
        {
            pendingRequests.Clear();
            foreach (var entry in originalEntries)
            {
                pendingRequests[entry.Key] = entry.Value;
            }
        }
    }
}
