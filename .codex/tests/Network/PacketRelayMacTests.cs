using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Emberglass.API.Shared;
using Emberglass.Network;
using ProjectM.Network;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Provides coverage for packet relay MAC verification and fragment reassembly.
/// </summary>
[Collection("Assembly setup")]
public sealed class PacketRelayMacTests
{
    static readonly byte[] FakeHmacKey = Encoding.UTF8.GetBytes("fake-hmac-key-for-tests");

    /// <summary>
    /// Ensures base64 fragments reassemble correctly using the net buffer.
    /// </summary>
    [Fact]
    public void FragmentBase64_ReassemblesUsingNetBuffer()
    {
        Type PacketRelayType = GetPacketRelayType();
        FragmentBase64Delegate FragmentBase64 = GetFragmentBase64(PacketRelayType);
        Type NetBufferType = GetNetBufferType();

        string Payload = new('A', Registry.Const.PACKET_BYTES * 2 + 5);
        List<string> Fragments = FragmentBase64(Payload);

        object NetBuffer = CreateNetBuffer(NetBufferType, Fragments.Count);
        List<int> Indexes = Enumerable.Range(0, Fragments.Count).Reverse().ToList();
        bool IsComplete = false;

        for (int i = 0; i < Indexes.Count; i++)
        {
            int Index = Indexes[i];
            IsComplete = AddPart(NetBufferType, NetBuffer, Index, Fragments[Index]);
            if (i < Indexes.Count - 1)
            {
                Assert.False(IsComplete);
            }
        }

        Assert.True(IsComplete);
        Assert.Equal(Payload, Concat(NetBufferType, NetBuffer));
        Assert.False(AddPart(NetBufferType, NetBuffer, 0, Fragments[0]));
    }

    /// <summary>
    /// Ensures fragments leave room for the packet prefix, header, delimiter, and MAC tag.
    /// </summary>
    [Fact]
    public void FragmentBase64_LeavesRoomForAuthenticatedWireEnvelope()
    {
        Type PacketRelayType = GetPacketRelayType();
        FragmentBase64Delegate FragmentBase64 = GetFragmentBase64(PacketRelayType);
        string Payload = new('A', Registry.Const.PACKET_BYTES * 2 + 5);
        List<string> Fragments = FragmentBase64(Payload);
        string MessageId = new('F', 16);
        uint TypeId = uint.MaxValue;
        string Tag = new('A', Registry.Const.MAC_TAG_BYTES * 2);

        for (int i = 0; i < Fragments.Count; i++)
        {
            string Header = $"{MessageId}|{i}/{Fragments.Count}|{TypeId}|";
            string WirePacket = $"{Registry.Const.PREFIX}{Header}{Fragments[i]}|{Tag}";
            Assert.True(WirePacket.Length <= Registry.Const.MAX_BYTES, WirePacket.Length.ToString());
        }
    }

    /// <summary>
    /// Ensures client-side MAC verification accepts valid tags and rejects invalid ones.
    /// </summary>
    [Fact]
    public void VerifyMac_ClientPath_ValidatesAndRejectsInvalidTags()
    {
        Type PacketRelayType = GetPacketRelayType();
        VerifyMacDelegate VerifyMac = GetVerifyMac(PacketRelayType);
        ComputeMacClientDelegate ComputeMacClient = GetComputeMacClient(PacketRelayType);
        const ulong PlatformId = 7711;
        User Sender = new();

        using IDisposable runtimeContextScope = BeginRuntimeContextOverride(isClient: true);
        using IDisposable platformIdScope = PacketRelay.BeginPlatformIdOverride(user => PlatformId);
        using PacketRelayHmacScope HmacScope = new(PacketRelayType);

        HmacScope.SetClientHmac(FakeHmacKey, true);
        string Unsigned = "packet|0/1|42|payload";
        string Tag = Convert.ToHexString(ComputeMacClient(Unsigned));

        Assert.False(string.IsNullOrEmpty(Tag));
        Assert.True(VerifyMac(Sender, Unsigned, Tag));
        Assert.False(VerifyMac(Sender, Unsigned, MutateTag(Tag)));
    }

    /// <summary>
    /// Ensures server-side MAC verification accepts valid tags and rejects invalid ones.
    /// </summary>
    [Fact]
    public void VerifyMac_ServerPath_ValidatesAndRejectsInvalidTags()
    {
        Type PacketRelayType = GetPacketRelayType();
        VerifyMacDelegate VerifyMac = GetVerifyMac(PacketRelayType);
        ComputeMacServerDelegate ComputeMacServer = GetComputeMacServer(PacketRelayType);
        const ulong PlatformId = 8080;
        User Sender = new();

        using IDisposable runtimeContextScope = BeginRuntimeContextOverride(isClient: false);
        using IDisposable platformIdScope = PacketRelay.BeginPlatformIdOverride(user => PlatformId);
        using PacketRelayHmacScope HmacScope = new(PacketRelayType);

        HmacScope.SetServerHmac(PlatformId, FakeHmacKey);
        string Unsigned = "packet|0/1|42|payload";
        string Tag = Convert.ToHexString(ComputeMacServer(Sender, Unsigned));

        Assert.False(string.IsNullOrEmpty(Tag));
        Assert.True(VerifyMac(Sender, Unsigned, Tag));
        Assert.False(VerifyMac(Sender, Unsigned, MutateTag(Tag)));
    }

    /// <summary>
    /// Ensures MAC generation fails when handshake keys are missing.
    /// </summary>
    [Fact]
    public void MacGeneration_FailsWithoutHandshakeKeys()
    {
        Type PacketRelayType = GetPacketRelayType();
        ComputeMacServerDelegate ComputeMacServer = GetComputeMacServer(PacketRelayType);
        ComputeMacClientDelegate ComputeMacClient = GetComputeMacClient(PacketRelayType);
        const ulong PlatformId = 9901;
        User Sender = new();

        using IDisposable platformIdScope = PacketRelay.BeginPlatformIdOverride(user => PlatformId);
        using PacketRelayHmacScope HmacScope = new(PacketRelayType);

        byte[] ServerTag = ComputeMacServer(Sender, "packet|0/1|42|payload");
        Assert.Empty(ServerTag);

        HmacScope.SetClientHmac(FakeHmacKey, false);
        byte[] ClientTag = ComputeMacClient("packet|0/1|42|payload");
        Assert.Empty(ClientTag);
    }

    /// <summary>
    /// Resolves the packet relay type from the Emberglass assembly.
    /// </summary>
    /// <returns>The packet relay type.</returns>
    static Type GetPacketRelayType()
    {
        Assembly EmberglassAssembly = typeof(Registry).Assembly;
        Type? PacketRelayType = EmberglassAssembly.GetType("Emberglass.Network.PacketRelay");
        return PacketRelayType ?? throw new InvalidOperationException("PacketRelay type not found.");
    }

    /// <summary>
    /// Resolves the net buffer type from the Emberglass assembly.
    /// </summary>
    /// <returns>The net buffer type.</returns>
    static Type GetNetBufferType()
    {
        Assembly EmberglassAssembly = typeof(Registry).Assembly;
        Type? NetBufferType = EmberglassAssembly.GetType("Emberglass.Network.NetBuffer");
        return NetBufferType ?? throw new InvalidOperationException("NetBuffer type not found.");
    }

    /// <summary>
    /// Creates a net buffer instance for the specified number of fragments.
    /// </summary>
    /// <param name="netBufferType">Net buffer type.</param>
    /// <param name="totalParts">Total fragment count.</param>
    /// <returns>Net buffer instance.</returns>
    static object CreateNetBuffer(Type netBufferType, int totalParts)
    {
        return Activator.CreateInstance(netBufferType,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new object[] { totalParts },
                null)
            ?? throw new InvalidOperationException("Unable to create NetBuffer.");
    }

    /// <summary>
    /// Adds a fragment to the net buffer.
    /// </summary>
    /// <param name="netBufferType">Net buffer type.</param>
    /// <param name="netBuffer">Net buffer instance.</param>
    /// <param name="index">Fragment index.</param>
    /// <param name="fragment">Fragment payload.</param>
    /// <returns>True when all parts have been received.</returns>
    static bool AddPart(Type netBufferType, object netBuffer, int index, string fragment)
    {
        MethodInfo Method = netBufferType.GetMethod("AddPart", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("AddPart method not found.");
        return (bool)Method.Invoke(netBuffer, new object[] { index, fragment })!;
    }

    /// <summary>
    /// Concatenates the fragments stored in the net buffer.
    /// </summary>
    /// <param name="netBufferType">Net buffer type.</param>
    /// <param name="netBuffer">Net buffer instance.</param>
    /// <returns>Reassembled payload.</returns>
    static string Concat(Type netBufferType, object netBuffer)
    {
        MethodInfo Method = netBufferType.GetMethod("Concat", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Concat method not found.");
        return (string)Method.Invoke(netBuffer, Array.Empty<object>())!;
    }

    /// <summary>
    /// Mutates a MAC tag to ensure it no longer matches.
    /// </summary>
    /// <param name="tag">Original MAC tag.</param>
    /// <returns>Mutated tag string.</returns>
    static string MutateTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return "0";
        }

        char[] Buffer = tag.ToCharArray();
        int LastIndex = Buffer.Length - 1;
        Buffer[LastIndex] = Buffer[LastIndex] == '0' ? '1' : '0';
        return new string(Buffer);
    }

    /// <summary>
    /// Retrieves a delegate for fragmenting base64 payloads.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for base64 fragmentation.</returns>
    static FragmentBase64Delegate GetFragmentBase64(Type packetRelayType)
        => CreateDelegate<FragmentBase64Delegate>(packetRelayType, "FragmentBase64");

    /// <summary>
    /// Retrieves a delegate for MAC verification.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for MAC verification.</returns>
    static VerifyMacDelegate GetVerifyMac(Type packetRelayType)
        => CreateDelegate<VerifyMacDelegate>(packetRelayType, "VerifyMac");

    /// <summary>
    /// Retrieves a delegate for computing server MAC tags.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for server MAC computation.</returns>
    static ComputeMacServerDelegate GetComputeMacServer(Type packetRelayType)
        => CreateDelegate<ComputeMacServerDelegate>(packetRelayType, "ComputeMacServer");

    /// <summary>
    /// Retrieves a delegate for computing client MAC tags.
    /// </summary>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <returns>Delegate for client MAC computation.</returns>
    static ComputeMacClientDelegate GetComputeMacClient(Type packetRelayType)
        => CreateDelegate<ComputeMacClientDelegate>(packetRelayType, "ComputeMacClient");

    /// <summary>
    /// Creates a delegate for a private static method.
    /// </summary>
    /// <typeparam name="TDelegate">Delegate type.</typeparam>
    /// <param name="packetRelayType">Packet relay type.</param>
    /// <param name="methodName">Method name to locate.</param>
    /// <returns>Delegate instance.</returns>
    static TDelegate CreateDelegate<TDelegate>(Type packetRelayType, string methodName)
        where TDelegate : Delegate
    {
        MethodInfo Method = packetRelayType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' not found.");
        return (TDelegate)Method.CreateDelegate(typeof(TDelegate));
    }

    /// <summary>
    /// Begins a temporary VWorld runtime-context override for testing.
    /// </summary>
    /// <param name="isClient">True to force client context; false to force server context.</param>
    /// <returns>Override scope.</returns>
    static IDisposable BeginRuntimeContextOverride(bool isClient)
    {
        MethodInfo Method = typeof(VWorld).GetMethod("BeginRuntimeContextOverride", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BeginRuntimeContextOverride method not found.");
        return (IDisposable)(Method.Invoke(null, new object[] { isClient })
            ?? throw new InvalidOperationException("Runtime context override scope was null."));
    }

    /// <summary>
    /// Delegate for base64 fragmentation.
    /// </summary>
    /// <param name="b64">Base64 payload.</param>
    /// <returns>Fragment list.</returns>
    delegate List<string> FragmentBase64Delegate(string b64);

    /// <summary>
    /// Delegate for MAC verification.
    /// </summary>
    /// <param name="sender">Sender user.</param>
    /// <param name="unsigned">Unsigned payload.</param>
    /// <param name="hex">MAC tag.</param>
    /// <returns>True when MAC matches.</returns>
    delegate bool VerifyMacDelegate(User sender, string unsigned, string hex);

    /// <summary>
    /// Delegate for server MAC computation.
    /// </summary>
    /// <param name="sender">Sender user.</param>
    /// <param name="input">Unsigned payload.</param>
    /// <returns>MAC tag bytes.</returns>
    delegate byte[] ComputeMacServerDelegate(User sender, string input);

    /// <summary>
    /// Delegate for client MAC computation.
    /// </summary>
    /// <param name="input">Unsigned payload.</param>
    /// <returns>MAC tag bytes.</returns>
    delegate byte[] ComputeMacClientDelegate(string input);

    /// <summary>
    /// Temporarily overrides packet relay HMAC state for testing.
    /// </summary>
    sealed class PacketRelayHmacScope : IDisposable
    {
        readonly ConcurrentDictionary<ulong, HMACSHA256> hmacs;
        readonly Dictionary<ulong, HMACSHA256> originalEntries;
        readonly FieldInfo hmacField;
        readonly FieldInfo clientHandshakeCompleteField;
        readonly HMACSHA256? originalClientHmac;
        readonly bool originalClientHandshakeComplete;

        /// <summary>
        /// Captures existing HMAC state and clears server entries.
        /// </summary>
        /// <param name="packetRelayType">Packet relay type.</param>
        public PacketRelayHmacScope(Type packetRelayType)
        {
            FieldInfo HmacsField = packetRelayType.GetField("_hmacs", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("_hmacs field not found.");
            hmacs = (ConcurrentDictionary<ulong, HMACSHA256>)(HmacsField.GetValue(null)
                ?? throw new InvalidOperationException("_hmacs field value missing."));

            originalEntries = new Dictionary<ulong, HMACSHA256>(hmacs);
            foreach (var entry in originalEntries)
            {
                hmacs.TryRemove(entry.Key, out _);
            }

            hmacField = packetRelayType.GetField("_hmac", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("_hmac field not found.");
            clientHandshakeCompleteField = packetRelayType.GetField("_clientHandshakeComplete", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("_clientHandshakeComplete field not found.");

            originalClientHmac = hmacField.GetValue(null) as HMACSHA256;
            originalClientHandshakeComplete = (bool)(clientHandshakeCompleteField.GetValue(null) ?? false);

            hmacField.SetValue(null, null);
            clientHandshakeCompleteField.SetValue(null, false);
        }

        /// <summary>
        /// Adds a server-side HMAC entry for the specified user.
        /// </summary>
        /// <param name="platformId">Platform identifier for the user.</param>
        /// <param name="key">HMAC key bytes.</param>
        public void SetServerHmac(ulong platformId, byte[] key)
        {
            hmacs[platformId] = new HMACSHA256(key);
        }

        /// <summary>
        /// Sets the client-side HMAC and handshake completion flag.
        /// </summary>
        /// <param name="key">HMAC key bytes.</param>
        /// <param name="handshakeComplete">Handshake completion flag.</param>
        public void SetClientHmac(byte[] key, bool handshakeComplete)
        {
            if (hmacField.GetValue(null) is HMACSHA256 existing)
            {
                existing.Dispose();
            }

            hmacField.SetValue(null, new HMACSHA256(key));
            clientHandshakeCompleteField.SetValue(null, handshakeComplete);
        }

        /// <summary>
        /// Restores the original HMAC state.
        /// </summary>
        public void Dispose()
        {
            foreach (var entry in hmacs)
            {
                if (!originalEntries.ContainsKey(entry.Key)
                    && hmacs.TryRemove(entry.Key, out var hmac))
                {
                    hmac.Dispose();
                }
            }

            foreach (var entry in originalEntries)
            {
                hmacs[entry.Key] = entry.Value;
            }

            if (hmacField.GetValue(null) is HMACSHA256 currentHmac
                && currentHmac != originalClientHmac)
            {
                currentHmac.Dispose();
            }

            hmacField.SetValue(null, originalClientHmac);
            clientHandshakeCompleteField.SetValue(null, originalClientHandshakeComplete);
        }
    }
}
