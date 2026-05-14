using Emberglass.API.Shared;
using ProjectM.Network;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Unity.Entities;
using static Emberglass.API.Client.ClientModules.ConnectionModules;
using static Emberglass.API.Server.ServerModules.ConnectionModules;
using static Emberglass.API.Shared.VEvents;
using static Emberglass.Network.Registry;

namespace Emberglass.Network;
internal static class PacketRelay
{
    public static event Action<Entity, User, string> OnPacketReceivedHandler;
    public static void OnClientPacketReceived(Entity packet, User sender, string payload) => OnPacketReceivedHandler?.Invoke(packet, sender, payload);
    public static void OnServerPacketReceived(Entity packet, User sender, string payload) => OnPacketReceivedHandler?.Invoke(packet, sender, payload);

    public static Action<User, string> _sendClientPacket = (_, _) => throw new InvalidOperationException("SendClientPacket isn't bootstrapped, only use this from the client!");
    public static Action<User, string> _sendServerPacket = (_, _) => throw new InvalidOperationException("SendServerPacket isn't bootstrapped, only use this from the server!");

    static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, NetBuffer>> _netBuffers = [];
    static readonly TimeSpan _bufferTime = TimeSpan.FromSeconds(LIFETIME);

#pragma warning disable CS0618
    static readonly byte[] _legacySharedKeyBytes = Encoding.UTF8.GetBytes(Const.SHARED_KEY);
#pragma warning restore CS0618
    static readonly byte[] _hkdfInfoBytes = Encoding.UTF8.GetBytes(Const.HKDF_INFO);
    static readonly ConcurrentDictionary<ulong, HMACSHA256> _hmacs = [];

    static readonly ConcurrentDictionary<ulong, byte[]> _localPublicKeys = [];
    static readonly ConcurrentDictionary<ulong, byte[]> _handshakeNonces = [];
    static readonly ConcurrentDictionary<ulong, int> _handshakeProtocols = [];
    static byte[] _publicKey = [];
    static byte[] _remotePublicKey = [];
    static byte[] _handshakeNonce = [];
    static byte[] _serverSignaturePrivateKey = [];
    static byte[] _serverSignaturePublicKey = [];
    static HandshakeSignatureConfig _serverSignatureConfig = new();
    static HandshakeSignatureConfig _clientSignatureConfig = new();
    static bool _clientHasExplicitServerPublicKey;

    static HMACSHA256 _hmac;
    static readonly ConcurrentDictionary<ulong, ECDiffieHellman> _ecdhInstances = [];
    static bool _clientHandshakeComplete = false;
    static int _clientHandshakeProtocol = Const.PROTOCOL_VERSION;
    static bool _receivedAuthenticatedServerHello = false;

    static long _nextMsgId = 0;
    const int LIFETIME = 100; // ~2MB share size limit

    static bool _initialized = false;
    static Func<User, ulong> _platformIdResolver = user => user.PlatformId;
    static readonly uint _clientHandshakeTypeId = Hash32(typeof(ClientHandshake).FullName!);
    static readonly uint _keyExchangeTypeId = Hash32(typeof(KeyExchange).FullName!);
    static readonly uint _trustOfferTypeId = Hash32(typeof(TrustOffer).FullName!);
    static int _packetDiagnosticsEmitted;
    static int _handshakeDiagnosticsEmitted;
    static int _macDiagnosticsEmitted;
    const int MAX_PACKET_DIAGNOSTICS = 40;
    const int MAX_HANDSHAKE_DIAGNOSTICS = 40;
    const int MAX_MAC_DIAGNOSTICS = 80;
    const int HANDSHAKE_FINGERPRINT_BYTES = 8;
    enum LegacyHandshakeMacStatus
    {
        Valid,
        SignatureTooShort,
        MacMismatch,
        PaddingMismatch
    }
    unsafe struct KeyExchange
    {
        public fixed byte AsymmetricKey[Const.EXCHANGE_LENGTH];
        public fixed byte Nonce[Const.HANDSHAKE_NONCE_BYTES];
        public fixed byte Signature[Const.HANDSHAKE_SIGNATURE_BYTES];
        public KeyExchange(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> signature)
        {
            fixed (byte* dest = AsymmetricKey)
            {
                WriteBytes(key, dest, Const.EXCHANGE_LENGTH);
            }

            fixed (byte* dest = Nonce)
            {
                WriteBytes(nonce, dest, Const.HANDSHAKE_NONCE_BYTES);
            }

            fixed (byte* dest = Signature)
            {
                WriteBytes(signature, dest, Const.HANDSHAKE_SIGNATURE_BYTES);
            }
        }
        public ReadOnlySpan<byte> KeySpan
        {
            get
            {
                fixed (byte* ptr = AsymmetricKey)
                {
                    return new ReadOnlySpan<byte>(ptr, Const.EXCHANGE_LENGTH);
                }
            }
        }
        public ReadOnlySpan<byte> NonceSpan
        {
            get
            {
                fixed (byte* ptr = Nonce)
                {
                    return new ReadOnlySpan<byte>(ptr, Const.HANDSHAKE_NONCE_BYTES);
                }
            }
        }
        public ReadOnlySpan<byte> SignatureSpan
        {
            get
            {
                fixed (byte* ptr = Signature)
                {
                    return new ReadOnlySpan<byte>(ptr, Const.HANDSHAKE_SIGNATURE_BYTES);
                }
            }
        }
        public byte[] KeyBytes => KeySpan.ToArray();
        public byte[] NonceBytes => NonceSpan.ToArray();
        public byte[] SignatureBytes => SignatureSpan.ToArray();

        static void WriteBytes(ReadOnlySpan<byte> source, byte* dest, int length)
        {
            int bytesToCopy = Math.Min(source.Length, length);
            for (int i = 0; i < bytesToCopy; i++)
            {
                dest[i] = source[i];
            }

            for (int i = bytesToCopy; i < length; i++)
            {
                dest[i] = 0;
            }
        }
    }
    sealed class TrustOffer
    {
        /// <summary>
        /// Gets or sets the stable server trust identifier.
        /// </summary>
        public string ServerTrustId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the server signing public key as Base64.
        /// </summary>
        public string ServerPublicKeyBase64 { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the SHA-256 fingerprint of the server signing public key.
        /// </summary>
        public string PublicKeyFingerprint { get; set; } = string.Empty;
    }
    public static void Bootstrap()
    {
        if (_initialized)
        {
            return;
        }

        OnPacketReceivedHandler += OnPacketReceived;

        if (VWorld.IsServer)
        {
            InitializeServerSignatureKeys();
            ModuleRegistry.Subscribe<UserConnected>(OnUserConnected);
            ModuleRegistry.Subscribe<UserDisconnected>(OnUserDisconnected);
        }

        if (VWorld.IsClient)
        {
            InitializeClientSignatureKeys();
            ModuleRegistry.Subscribe<ClientHandshake>(OnClientReady);
        }

        Register<ClientHandshake>(Direction.Serverbound, (u, o) => OnClientHandshake(u));
        Register<TrustOffer>(Direction.Clientbound, (u, o) => OnTrustOffer((TrustOffer)o));
        Register<KeyExchange>(Direction.Clientbound, (u, o) => OnKeyExchange(u, (KeyExchange)o));
        Register<KeyExchange>(Direction.Serverbound, (u, o) => OnKeyExchange(u, (KeyExchange)o));

        _initialized = true;
    }
    /// <summary>
    /// Overrides the platform ID resolver for testing scenarios.
    /// </summary>
    /// <param name="platformIdResolver">Resolver to use while the override is active.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous resolver.</returns>
    internal static IDisposable BeginPlatformIdOverride(Func<User, ulong> platformIdResolver)
    {
        ArgumentNullException.ThrowIfNull(platformIdResolver);
        return new PlatformIdOverrideScope(platformIdResolver);
    }
    /// <summary>
    /// Resolves the platform identifier for the provided user.
    /// </summary>
    /// <param name="user">User to resolve.</param>
    /// <returns>The resolved platform identifier.</returns>
    static ulong GetPlatformId(User user)
    {
        return _platformIdResolver(user);
    }
    sealed class PlatformIdOverrideScope : IDisposable
    {
        readonly Func<User, ulong> originalResolver;

        /// <summary>
        /// Initializes a new override scope for platform ID resolution.
        /// </summary>
        /// <param name="platformIdResolver">Resolver to apply.</param>
        public PlatformIdOverrideScope(Func<User, ulong> platformIdResolver)
        {
            originalResolver = _platformIdResolver;
            _platformIdResolver = platformIdResolver;
        }

        /// <summary>
        /// Restores the original resolver.
        /// </summary>
        public void Dispose()
        {
            _platformIdResolver = originalResolver;
        }
    }
    /// <summary>
    /// Loads or generates the server SignaturesP256 keypair for handshake signatures.
    /// </summary>
    static void InitializeServerSignatureKeys()
    {
        _serverSignatureConfig = HandshakeSignatureConfig.LoadForServer();
        if (!_serverSignatureConfig.TryGetServerPrivateKey(out byte[] privateKey)
            || !_serverSignatureConfig.TryGetServerPublicKey(out byte[] publicKey))
        {
            LogWarning("Server handshake signature keys are missing; authenticated handshakes are disabled.");
            return;
        }

        _serverSignaturePrivateKey = privateKey;
        _serverSignaturePublicKey = publicKey;
        LogInfo(
            $"[VNetwork.Trust] server trust ready; trustId={_serverSignatureConfig.ServerTrustId}, fingerprint={ShortFingerprint(publicKey)}.");
    }
    /// <summary>
    /// Loads the server public key for client-side handshake signature verification.
    /// </summary>
    static void InitializeClientSignatureKeys()
    {
        _clientSignatureConfig = HandshakeSignatureConfig.Load();
        _clientHasExplicitServerPublicKey = _clientSignatureConfig.TryGetServerPublicKey(out byte[] publicKey);
        if (!_clientHasExplicitServerPublicKey)
        {
            LogInfo("[VNetwork.Trust] no explicit server public key configured; waiting for server trust offer.");
            return;
        }

        _serverSignaturePublicKey = publicKey;
        LogInfo($"[VNetwork.Trust] client explicit server key loaded; fingerprint={ShortFingerprint(publicKey)}.");
    }
    static void OnUserConnected(UserConnected connected)
    {
        ulong id = connected.PlayerInfo.SteamId;

        ECDiffieHellman ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _ecdhInstances[id] = ecdh;
        _localPublicKeys[id] = ecdh.ExportSubjectPublicKeyInfo();
    }
    static void OnUserDisconnected(UserDisconnected disconnected)
        => CleanupUserHandshakeState(disconnected.PlayerInfo.SteamId);
    static void OnPacketReceived(Entity packet, User sender, string payload)
    {
        if (!payload.StartsWith(Const.PREFIX))
        {
            return;
        }

        payload = payload.AsSpan(Const.PREFIX.Length).ToString();
        int lastPipe = payload.LastIndexOf('|');
        if (lastPipe < 0)
        {
            return;
        }

        string unsigned = payload[..lastPipe];
        string[] parts = unsigned.Split('|', 4);
        if (parts.Length < 4)
        {
            LogPacketDiagnostic("dropped prefixed packet with invalid header.");
            return;
        }

        string msgGuid = parts[0];
        string partInfo = parts[1];
        string typeIdStr = parts[2];
        string thisChunk = parts[3];

        string[] tuple = partInfo.Split('/');
        if (tuple.Length != 2)
        {
            LogPacketDiagnostic($"dropped type={typeIdStr} with invalid chunk header.");
            return;
        }

        if (!uint.TryParse(typeIdStr, out uint typeId))
        {
            LogPacketDiagnostic("dropped prefixed packet with invalid type id.");
            return;
        }

        string hex = payload[(lastPipe + 1)..];
        bool isInternalHandshakePacket = IsInternalHandshakeTypeId(typeId);
        if (!isInternalHandshakePacket && !VerifyMac(sender, unsigned, hex))
        {
            LogPacketDiagnostic($"dropped type={typeId} due to MAC validation failure.");
            return;
        }

        if (!int.TryParse(tuple[0], out int idx) || !int.TryParse(tuple[1], out int total))
        {
            LogPacketDiagnostic($"dropped type={typeId} with invalid chunk numbers.");
            return;
        }

        if (total <= 0 || idx < 0 || idx >= total)
        {
            LogPacketDiagnostic($"dropped type={typeId} with out-of-range chunk {partInfo}.");
            return;
        }

        ulong senderId = GetPlatformId(sender);
        var senderBuffers = _netBuffers.GetOrAdd(senderId, _ => new ConcurrentDictionary<string, NetBuffer>());
        NetBuffer buffer = senderBuffers.GetOrAdd(msgGuid, _ => new NetBuffer(total));
        if (buffer.AddPart(idx, thisChunk))
        {
            senderBuffers.TryRemove(msgGuid, out _);
            if (senderBuffers.IsEmpty)
            {
                _netBuffers.TryRemove(senderId, out _);
            }

            Direction expectedDirection = VWorld.IsClient
                ? Direction.Clientbound
                : Direction.Serverbound;
            UnpackPacket(
                sender,
                expectedDirection,
                typeId,
                buffer.Concat());
        }

        packet.Destroy(true);
        SweepPackets();
    }
    static void UnpackPacket(User sender, Direction expectedDirection, uint typeId, string b64)
    {
        if (!TryGet(expectedDirection, typeId, out Handler handler) || handler == null)
        {
            LogPacketDiagnostic($"dropped type={typeId} direction={expectedDirection} because no handler is registered.");
            return;
        }

        try
        {
            object obj = handler.Unpack(Convert.FromBase64String(b64));
            handler.Invoke(sender, obj);
        }
        catch (Exception ex)
        {
            LogPacketDiagnostic($"failed dispatching type={typeId}: {ex.GetType().Name}.");
            throw;
        }
    }
    public static void SendPacketFromServer<T>(User user, T packet)
    {
        if (packet is null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        Type type = packet.GetType();
        uint typeId = Hash32(type.FullName!);

        var pack = Serialization.GetPacker(type);
        byte[] data = pack(packet);
        string b64 = Convert.ToBase64String(data);

        string msgGuid = Interlocked.Increment(ref _nextMsgId).ToString("X16");
        var slices = FragmentBase64(b64);
        int total = slices.Count;

        for (int i = 0; i < total; i++)
        {
            string header = $"{msgGuid}|{i}/{total}|{typeId}|";
            string preHmac = $"{header}{slices[i]}";
            byte[] tagBytes = ComputeMacServer(user, preHmac);

            if (tagBytes.Length == 0)
            {
                return;
            }

            string tag = Convert.ToHexString(tagBytes);
            _sendServerPacket(user, $"{Const.PREFIX}{preHmac}|{tag}");
        }
    }
    /// <summary>
    /// Sends an internal handshake packet to a client before a session MAC is available.
    /// </summary>
    /// <param name="user">User receiving the handshake packet.</param>
    /// <param name="packet">Handshake packet payload.</param>
    /// <typeparam name="T">Internal handshake packet type.</typeparam>
    static void SendHandshakePacketFromServer<T>(User user, T packet)
    {
        SendUnsignedHandshakePacket(user, packet, _sendServerPacket);
    }
    public static void SendPacketFromClient<T>(User user, T packet)
    {
        if (packet is null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        Type type = packet.GetType();
        uint typeId = Hash32(type.FullName!);

        var pack = Serialization.GetPacker(type);
        byte[] data = pack(packet!);
        string b64 = Convert.ToBase64String(data);

        string msgGuid = Interlocked.Increment(ref _nextMsgId).ToString("X16");
        var slices = FragmentBase64(b64);
        int total = slices.Count;

        for (int i = 0; i < total; i++)
        {
            string header = $"{msgGuid}|{i}/{total}|{typeId}|";
            string unsigned = $"{header}{slices[i]}";
            byte[] tagBytes = ComputeMacClient(unsigned);

            if (tagBytes.Length == 0)
            {
                return;
            }

            string tag = Convert.ToHexString(tagBytes);
            _sendClientPacket(user, $"{Const.PREFIX}{unsigned}|{tag}");
        }
    }
    /// <summary>
    /// Sends an internal handshake packet to the server before a session MAC is available.
    /// </summary>
    /// <param name="user">Server user receiving the handshake packet.</param>
    /// <param name="packet">Handshake packet payload.</param>
    /// <typeparam name="T">Internal handshake packet type.</typeparam>
    static void SendHandshakePacketFromClient<T>(User user, T packet)
    {
        SendUnsignedHandshakePacket(user, packet, _sendClientPacket);
    }
    /// <summary>
    /// Sends an internal handshake packet without the session MAC that the handshake establishes.
    /// </summary>
    /// <param name="user">Remote user receiving the handshake packet.</param>
    /// <param name="packet">Internal handshake packet payload.</param>
    /// <param name="sendPacket">Transport-specific packet sender.</param>
    /// <typeparam name="T">Internal handshake packet type.</typeparam>
    static void SendUnsignedHandshakePacket<T>(User user, T packet, Action<User, string> sendPacket)
    {
        if (packet is null)
        {
            throw new ArgumentNullException(nameof(packet));
        }

        Type type = packet.GetType();
        uint typeId = Hash32(type.FullName!);
        if (!IsInternalHandshakeTypeId(typeId))
        {
            throw new InvalidOperationException($"Unsigned handshake transport cannot send {type.FullName}.");
        }

        var pack = Serialization.GetPacker(type);
        byte[] data = pack(packet);
        string b64 = Convert.ToBase64String(data);

        string msgGuid = Interlocked.Increment(ref _nextMsgId).ToString("X16");
        var slices = FragmentBase64(b64);
        int total = slices.Count;
        int maxLength = 0;

        for (int i = 0; i < total; i++)
        {
            string header = $"{msgGuid}|{i}/{total}|{typeId}|";
            string unsigned = $"{header}{slices[i]}";
            string wirePacket = $"{Const.PREFIX}{unsigned}|";
            maxLength = Math.Max(maxLength, wirePacket.Length);
            sendPacket(user, wirePacket);
        }

    }
    static void OnClientReady(ClientHandshake handshake)
    {
        VWorld.Log.LogInfo("[VNetwork.Handshake] client module ready; sending handshake start.");
        SendHandshakePacketFromClient(VWorld.LocalUser.GetUser(), handshake);
    }
    static void OnClientHandshake(User user)
    {
        VWorld.Log.LogInfo($"[VNetwork.Handshake] server received handshake start for {GetPlatformId(user)}.");
        GetOrCreateEcdh(user, out byte[] localPublicKey);
        byte[] nonce = CreateHandshakeNonce();
        _handshakeNonces[GetPlatformId(user)] = nonce;
        byte[] serverHelloSignature = ComputeServerHelloSignature(localPublicKey, nonce, Const.PROTOCOL_VERSION);
        SendTrustOffer(user);
        SendHandshakePacketFromServer(user, new KeyExchange(localPublicKey, nonce, serverHelloSignature));
        if (Const.ALLOW_LEGACY_HANDSHAKE)
        {
            SendHandshakePacketFromServer(user, new KeyExchange(localPublicKey, nonce, []));
        }

        VWorld.Log.LogInfo($"[VNetwork.Handshake] server sent key exchange offer for {GetPlatformId(user)}.");
    }
    static void SendTrustOffer(User user)
    {
        if (_serverSignaturePublicKey.Length != SignaturesP256.PublicKeySize
            || string.IsNullOrWhiteSpace(_serverSignatureConfig.ServerTrustId))
        {
            return;
        }

        SendHandshakePacketFromServer(
            user,
            new TrustOffer
            {
                ServerTrustId = _serverSignatureConfig.ServerTrustId,
                ServerPublicKeyBase64 = Convert.ToBase64String(_serverSignaturePublicKey),
                PublicKeyFingerprint = HandshakeSignatureConfig.ComputePublicKeyFingerprint(_serverSignaturePublicKey)
            });
    }
    static void OnTrustOffer(TrustOffer offer)
    {
        if (string.IsNullOrWhiteSpace(offer.ServerTrustId)
            || string.IsNullOrWhiteSpace(offer.ServerPublicKeyBase64)
            || !TryDecodeTrustOfferPublicKey(offer, out byte[] publicKey))
        {
            LogWarning("[VNetwork.Trust] client rejected trust offer; reason=InvalidOffer.");
            return;
        }

        string actualFingerprint = HandshakeSignatureConfig.ComputePublicKeyFingerprint(publicKey);
        if (!string.Equals(offer.PublicKeyFingerprint, actualFingerprint, StringComparison.Ordinal))
        {
            LogWarning(
                $"[VNetwork.Trust] client rejected trust offer; reason=FingerprintMismatch, trustId={offer.ServerTrustId}, fingerprint={ShortFingerprint(actualFingerprint)}.");
            return;
        }

        if (_clientHasExplicitServerPublicKey)
        {
            if (CryptographicOperations.FixedTimeEquals(_serverSignaturePublicKey, publicKey))
            {
                LogInfo(
                    $"[VNetwork.Trust] client accepted explicit server key; trustId={offer.ServerTrustId}, fingerprint={ShortFingerprint(publicKey)}.");
                return;
            }

            LogWarning(
                $"[VNetwork.Trust] client rejected trust offer; reason=ExplicitKeyMismatch, trustId={offer.ServerTrustId}, fingerprint={ShortFingerprint(publicKey)}.");
            return;
        }

        TrustPinResult result = _clientSignatureConfig.TrustServerPublicKey(offer.ServerTrustId, publicKey);
        if (result is TrustPinResult.Pinned or TrustPinResult.AlreadyTrusted)
        {
            _serverSignaturePublicKey = publicKey;
            string action = result == TrustPinResult.Pinned ? "pinned" : "accepted pinned";
            LogInfo(
                $"[VNetwork.Trust] client {action} server key; trustId={offer.ServerTrustId}, fingerprint={ShortFingerprint(publicKey)}.");
            return;
        }

        LogWarning(
            $"[VNetwork.Trust] client rejected trust offer; reason={result}, trustId={offer.ServerTrustId}, fingerprint={ShortFingerprint(publicKey)}.");
    }
    static bool TryDecodeTrustOfferPublicKey(TrustOffer offer, out byte[] publicKey)
    {
        try
        {
            publicKey = Convert.FromBase64String(offer.ServerPublicKeyBase64);
            return publicKey.Length == SignaturesP256.PublicKeySize;
        }
        catch (FormatException)
        {
            publicKey = [];
            return false;
        }
    }
    static void OnKeyExchange(User user, KeyExchange exchange)
    {
        ECDiffieHellman localEcdh = GetOrCreateEcdh(user, out byte[] localPublicKey);

        using ECDiffieHellman remotePublic = ECDiffieHellman.Create();
        byte[] remotePublicKey;
        try
        {
            remotePublicKey = ExtractImportedPublicKeyBytes(exchange.KeySpan);
            remotePublic.ImportSubjectPublicKeyInfo(remotePublicKey, out _);
        }
        catch (CryptographicException)
        {
            VWorld.Log.LogInfo("[VNetwork.Handshake] rejected key exchange with invalid public key.");
            return;
        }

        if (VWorld.IsClient)
        {
            if (!IsSignatureEmpty(exchange.SignatureSpan)
                && _handshakeNonce.Length > 0
                && _remotePublicKey.Length > 0)
            {
                HandleClientHandshakeAck(user, exchange, localEcdh, localPublicKey, remotePublicKey, remotePublic);
            }
            else
            {
                HandleClientKeyExchange(user, exchange, localPublicKey, remotePublicKey);
            }
            return;
        }

        if (VWorld.IsServer)
        {
            HandleServerKeyExchange(user, exchange, localEcdh, localPublicKey, remotePublicKey, remotePublic);
        }
    }
    /// <summary>
    /// Ensures an ECDH instance exists for the user and returns the local public key.
    /// </summary>
    /// <param name="user">User associated with the handshake.</param>
    /// <param name="localPublicKey">Output public key for the local ECDH instance.</param>
    /// <returns>Local ECDH instance for the user.</returns>
    static ECDiffieHellman GetOrCreateEcdh(User user, out byte[] localPublicKey)
    {
        if (!_ecdhInstances.TryGetValue(GetPlatformId(user), out var localEcdh))
        {
            localEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            _ecdhInstances[GetPlatformId(user)] = localEcdh;
        }

        if (VWorld.IsServer)
        {
            if (!_localPublicKeys.TryGetValue(GetPlatformId(user), out localPublicKey))
            {
                localPublicKey = localEcdh.ExportSubjectPublicKeyInfo();
                _localPublicKeys[GetPlatformId(user)] = localPublicKey;
            }

            return localEcdh;
        }

        if (_publicKey.Length == 0)
        {
            _publicKey = localEcdh.ExportSubjectPublicKeyInfo();
        }

        localPublicKey = _publicKey;
        return localEcdh;
    }
    /// <summary>
    /// Creates a handshake nonce for integrity verification.
    /// </summary>
    /// <returns>Random nonce bytes.</returns>
    static byte[] CreateHandshakeNonce()
    {
        byte[] nonce = new byte[Const.HANDSHAKE_NONCE_BYTES];
        RandomNumberGenerator.Fill(nonce);
        return nonce;
    }
    /// <summary>
    /// Extracts the canonical imported public key bytes from a fixed packet field.
    /// </summary>
    /// <param name="keyField">Fixed packet key field bytes.</param>
    /// <returns>Public key bytes consumed by the SubjectPublicKeyInfo import.</returns>
    static byte[] ExtractImportedPublicKeyBytes(ReadOnlySpan<byte> keyField)
    {
        using ECDiffieHellman probe = ECDiffieHellman.Create();
        probe.ImportSubjectPublicKeyInfo(keyField, out int bytesRead);
        return keyField[..bytesRead].ToArray();
    }
    /// <summary>
    /// Handles the client-side key exchange workflow and validates server acknowledgements.
    /// </summary>
    /// <param name="user">Remote server user.</param>
    /// <param name="exchange">Incoming key exchange payload.</param>
    /// <param name="localPublicKey">Local public key bytes.</param>
    /// <param name="remotePublicKey">Canonical remote public key bytes.</param>
    static void HandleClientKeyExchange(User user, KeyExchange exchange, byte[] localPublicKey, byte[] remotePublicKey)
    {
        if (_clientHandshakeComplete)
        {
            VWorld.Log.LogInfo("[VNetwork.Handshake] client ignored key exchange after completion.");
            return;
        }

        if (IsSignatureEmpty(exchange.SignatureSpan))
        {
            if (_receivedAuthenticatedServerHello)
            {
                VWorld.Log.LogInfo("[VNetwork.Handshake] client ignored legacy offer after authenticated offer.");
                return;
            }

            _remotePublicKey = remotePublicKey;
            _handshakeNonce = exchange.NonceBytes;
            _clientHandshakeProtocol = Const.LEGACY_PROTOCOL_VERSION;
            byte[] responseMac = ComputeLegacyHandshakeMac(remotePublicKey, localPublicKey, exchange.NonceSpan);
            SendHandshakePacketFromClient(user, new KeyExchange(localPublicKey, exchange.NonceSpan, responseMac));
            VWorld.Log.LogInfo("[VNetwork.Handshake] client sent legacy key exchange response.");
            return;
        }

        if (!VerifyServerHelloSignature(remotePublicKey, exchange.NonceSpan, exchange.SignatureSpan))
        {
            VWorld.Log.LogInfo("[VNetwork.Handshake] client rejected authenticated server hello.");
            return;
        }

        _receivedAuthenticatedServerHello = true;
        _clientHandshakeProtocol = Const.PROTOCOL_VERSION;
        _remotePublicKey = remotePublicKey;
        _handshakeNonce = exchange.NonceBytes;
        SendHandshakePacketFromClient(user, new KeyExchange(localPublicKey, exchange.NonceSpan, []));
        VWorld.Log.LogInfo("[VNetwork.Handshake] client sent authenticated key exchange response.");
    }
    /// <summary>
    /// Handles the client-side acknowledgement and validates the server response.
    /// </summary>
    /// <param name="user">Remote server user.</param>
    /// <param name="exchange">Incoming key exchange payload.</param>
    /// <param name="localEcdh">Local ECDH instance.</param>
    /// <param name="localPublicKey">Local public key bytes.</param>
    /// <param name="remotePublicKey">Canonical remote public key bytes.</param>
    /// <param name="remotePublic">Remote ECDH public key.</param>
    static void HandleClientHandshakeAck(User user, KeyExchange exchange, ECDiffieHellman localEcdh, byte[] localPublicKey, byte[] remotePublicKey, ECDiffieHellman remotePublic)
    {
        if (_handshakeNonce.Length == 0 || _remotePublicKey.Length == 0)
        {
            VWorld.Log.LogInfo("[VNetwork.Handshake] client rejected handshake ack because state was missing.");
            return;
        }

        if (!CryptographicOperations.FixedTimeEquals(exchange.NonceSpan, _handshakeNonce))
        {
            VWorld.Log.LogInfo("[VNetwork.Handshake] client rejected handshake ack nonce.");
            return;
        }

        if (!CryptographicOperations.FixedTimeEquals(remotePublicKey, _remotePublicKey))
        {
            VWorld.Log.LogInfo("[VNetwork.Handshake] client rejected handshake ack key.");
            return;
        }

        if (_clientHandshakeProtocol == Const.LEGACY_PROTOCOL_VERSION)
        {
            byte[] expectedMac = ComputeLegacyHandshakeMac(remotePublicKey, localPublicKey, exchange.NonceSpan);
            if (!VerifyLegacyHandshakeMac(exchange.SignatureSpan, expectedMac))
            {
                VWorld.Log.LogInfo("[VNetwork.Handshake] client rejected legacy handshake ack.");
                return;
            }
        }
        else
        {
            if (!VerifyHandshakeSignature(remotePublicKey, localPublicKey, exchange.NonceSpan, exchange.SignatureSpan))
            {
                VWorld.Log.LogInfo("[VNetwork.Handshake] client rejected authenticated handshake ack.");
                return;
            }
        }

        VWorld.Log.LogInfo("[VNetwork.Handshake] client accepted handshake ack; completing session.");
        byte[] sharedSecret = localEcdh.DeriveKeyMaterial(remotePublic.PublicKey);
        byte[] sessionKey = DeriveSessionKey(sharedSecret, exchange.NonceSpan, remotePublicKey, localPublicKey, _clientHandshakeProtocol);
        SetKey(user, sessionKey);
        _handshakeNonce = [];
        _remotePublicKey = [];
    }
    /// <summary>
    /// Handles the server-side key exchange workflow and validates client handshakes.
    /// </summary>
    /// <param name="user">Remote client user.</param>
    /// <param name="exchange">Incoming key exchange payload.</param>
    /// <param name="localEcdh">Local ECDH instance.</param>
    /// <param name="localPublicKey">Local public key bytes.</param>
    /// <param name="remotePublicKey">Canonical remote public key bytes.</param>
    /// <param name="remotePublic">Remote ECDH public key.</param>
    static void HandleServerKeyExchange(User user, KeyExchange exchange, ECDiffieHellman localEcdh, byte[] localPublicKey, byte[] remotePublicKey, ECDiffieHellman remotePublic)
    {
        if (_hmacs.ContainsKey(GetPlatformId(user)))
        {
            VWorld.Log.LogInfo($"[VNetwork.Handshake] server ignored duplicate key exchange for {GetPlatformId(user)}.");
            return;
        }

        if (!_handshakeNonces.TryGetValue(GetPlatformId(user), out byte[] nonce))
        {
            VWorld.Log.LogInfo($"[VNetwork.Handshake] server rejected key exchange without nonce for {GetPlatformId(user)}.");
            return;
        }

        if (!CryptographicOperations.FixedTimeEquals(exchange.NonceSpan, nonce))
        {
            VWorld.Log.LogInfo($"[VNetwork.Handshake] server rejected key exchange nonce for {GetPlatformId(user)}.");
            return;
        }

        if (!TryResolveHandshakeProtocol(localPublicKey, remotePublicKey, nonce, exchange.SignatureSpan, out int protocolVersion))
        {
            VWorld.Log.LogInfo($"[VNetwork.Handshake] server rejected key exchange protocol for {GetPlatformId(user)}.");
            return;
        }

        byte[] sharedSecret = localEcdh.DeriveKeyMaterial(remotePublic.PublicKey);
        byte[] sessionKey = DeriveSessionKey(sharedSecret, nonce, localPublicKey, remotePublicKey, protocolVersion);
        SetKey(user, sessionKey);
        _handshakeProtocols[GetPlatformId(user)] = protocolVersion;
        _handshakeNonces.TryRemove(GetPlatformId(user), out _);
        byte[] serverAckSignature = protocolVersion == Const.LEGACY_PROTOCOL_VERSION
            ? ComputeLegacyHandshakeMac(localPublicKey, remotePublicKey, nonce)
            : ComputeHandshakeSignature(localPublicKey, remotePublicKey, nonce, protocolVersion);
        SendHandshakePacketFromServer(user, new KeyExchange(localPublicKey, nonce, serverAckSignature));
        VWorld.Log.LogInfo($"[VNetwork.Handshake] server accepted key exchange for {GetPlatformId(user)}; raising ready.");
        API.Shared.VNetwork.RaiseServerReady(user);
    }
    /// <summary>
    /// Computes the handshake signature for the provided server key, client key, and nonce.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <returns>Signature bytes for the handshake.</returns>
    static byte[] ComputeHandshakeSignature(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, ReadOnlySpan<byte> nonce, int protocolVersion)
    {
        byte[] buffer = BuildHandshakePayload(serverPublicKey, clientPublicKey, nonce, protocolVersion);
        return SignHandshakePayload(buffer);
    }
    /// <summary>
    /// Computes the legacy handshake MAC for the provided server key, client key, and nonce.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <returns>MAC bytes for the legacy handshake.</returns>
    static byte[] ComputeLegacyHandshakeMac(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, ReadOnlySpan<byte> nonce)
    {
        byte[] buffer = new byte[serverPublicKey.Length + clientPublicKey.Length + nonce.Length];
        Buffer.BlockCopy(serverPublicKey.ToArray(), 0, buffer, 0, serverPublicKey.Length);
        Buffer.BlockCopy(clientPublicKey.ToArray(), 0, buffer, serverPublicKey.Length, clientPublicKey.Length);
        Buffer.BlockCopy(nonce.ToArray(), 0, buffer, serverPublicKey.Length + clientPublicKey.Length, nonce.Length);
        using HMACSHA256 hmac = new(_legacySharedKeyBytes);
        return hmac.ComputeHash(buffer);
    }
    /// <summary>
    /// Computes the signature for the server hello in the authenticated handshake.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <returns>Signature bytes for the server hello.</returns>
    static byte[] ComputeServerHelloSignature(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> nonce, int protocolVersion)
    {
        byte[] buffer = BuildServerHelloPayload(serverPublicKey, nonce, protocolVersion);
        return SignHandshakePayload(buffer);
    }
    /// <summary>
    /// Signs a handshake payload using the configured server private key.
    /// </summary>
    /// <param name="payload">Serialized handshake payload bytes.</param>
    /// <returns>Signature bytes or an empty array if signing is unavailable.</returns>
    static byte[] SignHandshakePayload(ReadOnlySpan<byte> payload)
    {
        if (_serverSignaturePrivateKey.Length == 0)
        {
            return [];
        }

        byte[] signature = new byte[Const.HANDSHAKE_SIGNATURE_BYTES];
        SignaturesP256.Sign(payload, _serverSignaturePrivateKey, signature);
        return signature;
    }
    /// <summary>
    /// Verifies the handshake signature for authenticated acknowledgements.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="signature">Signature bytes to verify.</param>
    /// <returns>True when the signature matches the payload.</returns>
    static bool VerifyHandshakeSignature(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> signature)
    {
        byte[] payload = BuildHandshakePayload(serverPublicKey, clientPublicKey, nonce, Const.PROTOCOL_VERSION);
        return VerifyHandshakeSignaturePayload(payload, signature, "handshake-ack");
    }
    /// <summary>
    /// Verifies a serialized handshake payload against the configured server public key.
    /// </summary>
    /// <param name="payload">Serialized handshake payload bytes.</param>
    /// <param name="signature">Signature bytes to verify.</param>
    /// <returns>True when the signature is valid.</returns>
    static bool VerifyHandshakeSignaturePayload(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature, string context)
    {
        if (_serverSignaturePublicKey.Length == 0)
        {
            LogHandshakeDiagnostic(
                $"{context} verify rejected; reason=MissingServerPublicKey, payloadLength={payload.Length}, signatureLength={signature.Length}.");
            return false;
        }

        if (signature.Length != Const.HANDSHAKE_SIGNATURE_BYTES)
        {
            LogHandshakeDiagnostic(
                $"{context} verify rejected; reason=UnexpectedSignatureLength, payloadLength={payload.Length}, signatureLength={signature.Length}, serverPublicKeyLength={_serverSignaturePublicKey.Length}.");
            return false;
        }

        bool verified = SignaturesP256.Verify(payload, _serverSignaturePublicKey, signature);
        if (!verified)
        {
            LogHandshakeDiagnostic(
                $"{context} verify rejected; reason=SignatureMismatch, payloadLength={payload.Length}, signatureLength={signature.Length}, serverPublicKeyLength={_serverSignaturePublicKey.Length}.");
        }

        return verified;
    }
    /// <summary>
    /// Verifies the server hello signature for authenticated handshakes.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="signature">Signature bytes to verify.</param>
    /// <returns>True when the signature is valid.</returns>
    static bool VerifyServerHelloSignature(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> signature)
    {
        byte[] payload = BuildServerHelloPayload(serverPublicKey, nonce, Const.PROTOCOL_VERSION);
        return VerifyHandshakeSignaturePayload(payload, signature, "server-hello");
    }
    /// <summary>
    /// Determines which handshake protocol version is in use based on the signature or legacy MAC.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="signature">Signature bytes received.</param>
    /// <param name="protocolVersion">Resolved protocol version.</param>
    /// <returns>True when a supported protocol version is detected.</returns>
    static bool TryResolveHandshakeProtocol(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> signature, out int protocolVersion)
    {
        if (IsSignatureEmpty(signature))
        {
            if (_serverSignaturePrivateKey.Length == 0)
            {
                LogInfo("[VNetwork.Handshake] protocol resolve rejected empty signature; serverPrivateKeyPresent=False.");
                protocolVersion = 0;
                return false;
            }

            LogInfo("[VNetwork.Handshake] protocol resolve selected authenticated protocol from empty client response.");
            protocolVersion = Const.PROTOCOL_VERSION;
            return true;
        }

        byte[] legacyMac = ComputeLegacyHandshakeMac(serverPublicKey, clientPublicKey, nonce);
        LegacyHandshakeMacStatus status = GetLegacyHandshakeMacStatus(signature, legacyMac);
        if (status == LegacyHandshakeMacStatus.Valid)
        {
            LogInfo("[VNetwork.Handshake] protocol resolve selected legacy protocol.");
            protocolVersion = Const.LEGACY_PROTOCOL_VERSION;
            return true;
        }

        LogInfo(
            $"[VNetwork.Handshake] protocol resolve rejected legacy response; reason={status}, signatureLength={signature.Length}, expectedMacLength={legacyMac.Length}, serverPrivateKeyPresent={_serverSignaturePrivateKey.Length > 0}.");

        protocolVersion = 0;
        return false;
    }
    /// <summary>
    /// Derives a per-session HMAC key from the ECDH secret using HKDF.
    /// </summary>
    /// <param name="ecdhSecret">ECDH shared secret bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <returns>Derived key for the session.</returns>
    static byte[] DeriveSessionKey(ReadOnlySpan<byte> ecdhSecret, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, int protocolVersion)
    {
        byte[] salt = BuildHkdfSalt(protocolVersion, nonce);
        byte[] info = BuildHkdfInfo(protocolVersion, serverPublicKey, clientPublicKey);
        return HkdfExpand(HkdfExtract(salt, ecdhSecret), info, Const.STANDARD_LENGTH);
    }
    /// <summary>
    /// Builds the payload used for authenticated handshake signatures.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <returns>Serialized payload bytes.</returns>
    static byte[] BuildHandshakePayload(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey, ReadOnlySpan<byte> nonce, int protocolVersion)
    {
        byte[] versionBytes = BitConverter.GetBytes(protocolVersion);
        byte[] buffer = new byte[versionBytes.Length + serverPublicKey.Length + clientPublicKey.Length + nonce.Length];
        Buffer.BlockCopy(versionBytes, 0, buffer, 0, versionBytes.Length);
        Buffer.BlockCopy(serverPublicKey.ToArray(), 0, buffer, versionBytes.Length, serverPublicKey.Length);
        Buffer.BlockCopy(clientPublicKey.ToArray(), 0, buffer, versionBytes.Length + serverPublicKey.Length, clientPublicKey.Length);
        Buffer.BlockCopy(nonce.ToArray(), 0, buffer, versionBytes.Length + serverPublicKey.Length + clientPublicKey.Length, nonce.Length);
        return buffer;
    }
    /// <summary>
    /// Builds the payload used for authenticated server hello signatures.
    /// </summary>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <returns>Serialized payload bytes.</returns>
    static byte[] BuildServerHelloPayload(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> nonce, int protocolVersion)
    {
        byte[] versionBytes = BitConverter.GetBytes(protocolVersion);
        byte[] buffer = new byte[versionBytes.Length + serverPublicKey.Length + nonce.Length];
        Buffer.BlockCopy(versionBytes, 0, buffer, 0, versionBytes.Length);
        Buffer.BlockCopy(serverPublicKey.ToArray(), 0, buffer, versionBytes.Length, serverPublicKey.Length);
        Buffer.BlockCopy(nonce.ToArray(), 0, buffer, versionBytes.Length + serverPublicKey.Length, nonce.Length);
        return buffer;
    }
    /// <summary>
    /// Builds the HKDF salt for the session key derivation.
    /// </summary>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <param name="nonce">Handshake nonce bytes.</param>
    /// <returns>HKDF salt bytes.</returns>
    static byte[] BuildHkdfSalt(int protocolVersion, ReadOnlySpan<byte> nonce)
    {
        if (protocolVersion == Const.LEGACY_PROTOCOL_VERSION)
        {
            using HMACSHA256 hmac = new(_legacySharedKeyBytes);
            return hmac.ComputeHash(nonce.ToArray());
        }

        return SHA256.HashData(nonce.ToArray());
    }
    /// <summary>
    /// Builds the HKDF info bytes for the session key derivation.
    /// </summary>
    /// <param name="protocolVersion">Handshake protocol version.</param>
    /// <param name="serverPublicKey">Server public key bytes.</param>
    /// <param name="clientPublicKey">Client public key bytes.</param>
    /// <returns>HKDF info bytes.</returns>
    static byte[] BuildHkdfInfo(int protocolVersion, ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> clientPublicKey)
    {
        byte[] versionBytes = BitConverter.GetBytes(protocolVersion);
        byte[] info = new byte[_hkdfInfoBytes.Length + versionBytes.Length + serverPublicKey.Length + clientPublicKey.Length];
        Buffer.BlockCopy(_hkdfInfoBytes, 0, info, 0, _hkdfInfoBytes.Length);
        Buffer.BlockCopy(versionBytes, 0, info, _hkdfInfoBytes.Length, versionBytes.Length);
        Buffer.BlockCopy(serverPublicKey.ToArray(), 0, info, _hkdfInfoBytes.Length + versionBytes.Length, serverPublicKey.Length);
        Buffer.BlockCopy(clientPublicKey.ToArray(), 0, info, _hkdfInfoBytes.Length + versionBytes.Length + serverPublicKey.Length, clientPublicKey.Length);
        return info;
    }
    /// <summary>
    /// Performs the HKDF extract step.
    /// </summary>
    /// <param name="salt">Salt bytes for HKDF.</param>
    /// <param name="inputKeyMaterial">Input key material.</param>
    /// <returns>Pseudorandom key derived from input material.</returns>
    static byte[] HkdfExtract(byte[] salt, ReadOnlySpan<byte> inputKeyMaterial)
    {
        using HMACSHA256 hmac = new(salt);
        return hmac.ComputeHash(inputKeyMaterial.ToArray());
    }
    /// <summary>
    /// Performs the HKDF expand step.
    /// </summary>
    /// <param name="prk">Pseudorandom key from HKDF extract.</param>
    /// <param name="info">Context-specific info bytes.</param>
    /// <param name="length">Number of bytes to output.</param>
    /// <returns>Expanded keying material.</returns>
    static byte[] HkdfExpand(ReadOnlySpan<byte> prk, byte[] info, int length)
    {
        using HMACSHA256 hmac = new(prk.ToArray());
        int hashLength = hmac.HashSize / 8;
        int blockCount = (int)Math.Ceiling((double)length / hashLength);
        byte[] output = new byte[length];
        byte[] previous = [];
        int offset = 0;

        for (int i = 1; i <= blockCount; i++)
        {
            byte[] input = new byte[previous.Length + info.Length + 1];
            Buffer.BlockCopy(previous, 0, input, 0, previous.Length);
            Buffer.BlockCopy(info, 0, input, previous.Length, info.Length);
            input[^1] = (byte)i;

            previous = hmac.ComputeHash(input);
            int bytesToCopy = Math.Min(hashLength, length - offset);
            Buffer.BlockCopy(previous, 0, output, offset, bytesToCopy);
            offset += bytesToCopy;
        }

        return output;
    }
    /// <summary>
    /// Checks whether the signature span is all zeros.
    /// </summary>
    /// <param name="signature">Signature bytes to inspect.</param>
    /// <returns>True when all bytes are zero.</returns>
    static bool IsSignatureEmpty(ReadOnlySpan<byte> signature)
    {
        for (int i = 0; i < signature.Length; i++)
        {
            if (signature[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
    /// <summary>
    /// Verifies legacy MAC data stored in a fixed-length signature field.
    /// </summary>
    /// <param name="signatureField">Fixed-length signature field bytes.</param>
    /// <param name="expectedMac">Expected legacy MAC bytes.</param>
    /// <returns>True when the legacy MAC matches and remaining bytes are zero.</returns>
    static bool VerifyLegacyHandshakeMac(ReadOnlySpan<byte> signatureField, ReadOnlySpan<byte> expectedMac)
        => GetLegacyHandshakeMacStatus(signatureField, expectedMac) == LegacyHandshakeMacStatus.Valid;
    /// <summary>
    /// Classifies legacy MAC verification without exposing MAC contents.
    /// </summary>
    /// <param name="signatureField">Fixed-length signature field bytes.</param>
    /// <param name="expectedMac">Expected legacy MAC bytes.</param>
    /// <returns>The legacy MAC verification status.</returns>
    static LegacyHandshakeMacStatus GetLegacyHandshakeMacStatus(ReadOnlySpan<byte> signatureField, ReadOnlySpan<byte> expectedMac)
    {
        if (signatureField.Length < expectedMac.Length)
        {
            return LegacyHandshakeMacStatus.SignatureTooShort;
        }

        if (!CryptographicOperations.FixedTimeEquals(signatureField[..expectedMac.Length], expectedMac))
        {
            return LegacyHandshakeMacStatus.MacMismatch;
        }

        for (int i = expectedMac.Length; i < signatureField.Length; i++)
        {
            if (signatureField[i] != 0)
            {
                return LegacyHandshakeMacStatus.PaddingMismatch;
            }
        }

        return LegacyHandshakeMacStatus.Valid;
    }
    static void SetKey(User sender, byte[] key)
    {
        if (VWorld.IsServer)
        {
            HMACSHA256 hmac = new(key);
            _hmacs.AddOrUpdate(GetPlatformId(sender), hmac, (_, old) =>
            {
                old.Dispose();

                return hmac;
            });
        }

        if (VWorld.IsClient)
        {
            bool wasHandshakeComplete = _clientHandshakeComplete;
            _hmac = new HMACSHA256(key);
            _clientHandshakeComplete = true;
            if (!wasHandshakeComplete)
            {
                VWorld.Log.LogInfo("[VNetwork.Handshake] client session key established; raising ready.");
                API.Shared.VNetwork.RaiseClientReady();
            }
        }
    }
    /// <summary>
    /// Determines whether a packet type is part of the unauthenticated handshake bootstrap.
    /// </summary>
    /// <param name="typeId">Stable packet type identifier.</param>
    /// <returns>True when the packet is an internal handshake bootstrap packet.</returns>
    static bool IsInternalHandshakeTypeId(uint typeId)
    {
        return typeId == _clientHandshakeTypeId
            || typeId == _trustOfferTypeId
            || typeId == _keyExchangeTypeId;
    }
    /// <summary>
    /// Writes bounded packet diagnostics without payload contents.
    /// </summary>
    /// <param name="message">Diagnostic message.</param>
    static void LogPacketDiagnostic(string message)
    {
        if (_packetDiagnosticsEmitted >= MAX_PACKET_DIAGNOSTICS)
        {
            return;
        }

        _packetDiagnosticsEmitted++;
        LogInfo($"[VNetwork.Packet] {message}");
    }
    /// <summary>
    /// Writes bounded handshake diagnostics without key, signature, or payload contents.
    /// </summary>
    /// <param name="message">Diagnostic message.</param>
    static void LogHandshakeDiagnostic(string message)
    {
        if (_handshakeDiagnosticsEmitted >= MAX_HANDSHAKE_DIAGNOSTICS)
        {
            return;
        }

        _handshakeDiagnosticsEmitted++;
        LogInfo($"[VNetwork.Handshake.Diagnostic] {message}");
    }
    /// <summary>
    /// Writes bounded MAC diagnostics without key, payload, or MAC contents.
    /// </summary>
    /// <param name="message">Diagnostic message.</param>
    static void LogMacDiagnostic(string message)
    {
        if (_macDiagnosticsEmitted >= MAX_MAC_DIAGNOSTICS)
        {
            return;
        }

        _macDiagnosticsEmitted++;
        LogInfo($"[VNetwork.Mac] {message}");
    }
    /// <summary>
    /// Logs an informational message when the runtime logger is available.
    /// </summary>
    /// <param name="message">Message to write.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void LogInfo(string message)
    {
        try
        {
            VWorld.Log?.LogInfo(message);
        }
        catch (Exception ex) when (ex is FileNotFoundException or MissingMethodException or TypeLoadException)
        {
        }
    }
    /// <summary>
    /// Logs a warning message when the runtime logger is available.
    /// </summary>
    /// <param name="message">Message to write.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void LogWarning(string message)
    {
        try
        {
            VWorld.Log?.LogWarning(message);
        }
        catch (Exception ex) when (ex is FileNotFoundException or MissingMethodException or TypeLoadException)
        {
        }
    }
    /// <summary>
    /// Builds a short fingerprint for comparing handshake payload identity across runtimes.
    /// </summary>
    /// <param name="payload">Payload bytes to fingerprint.</param>
    /// <returns>Uppercase hex SHA-256 prefix.</returns>
    static string FingerprintPayload(ReadOnlySpan<byte> payload)
    {
        byte[] hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash, 0, Math.Min(HANDSHAKE_FINGERPRINT_BYTES, hash.Length));
    }
    /// <summary>
    /// Builds a short fingerprint for comparing text payload identity across runtimes.
    /// </summary>
    /// <param name="payload">Text payload to fingerprint.</param>
    /// <returns>Uppercase hex SHA-256 prefix.</returns>
    static string FingerprintText(string payload)
        => FingerprintPayload(Encoding.UTF8.GetBytes(payload));
    /// <summary>
    /// Builds a short fingerprint for comparing byte values without exposing them.
    /// </summary>
    /// <param name="bytes">Bytes to fingerprint.</param>
    /// <returns>Uppercase hex SHA-256 prefix, or Empty when no bytes are present.</returns>
    static string FingerprintBytes(ReadOnlySpan<byte> bytes)
        => bytes.Length == 0 ? "Empty" : FingerprintPayload(bytes);
    static string ShortFingerprint(ReadOnlySpan<byte> bytes)
        => ShortFingerprint(HandshakeSignatureConfig.ComputePublicKeyFingerprint(bytes));
    static string ShortFingerprint(string fingerprint)
        => fingerprint.Length <= HANDSHAKE_FINGERPRINT_BYTES * 2
            ? fingerprint
            : fingerprint[..(HANDSHAKE_FINGERPRINT_BYTES * 2)];
    /// <summary>
    /// Removes all per-user handshake and buffer state for the provided SteamId.
    /// </summary>
    /// <param name="steamId">SteamId to clean up.</param>
    static void CleanupUserHandshakeState(ulong steamId)
    {
        if (_netBuffers.TryRemove(steamId, out var senderBuffers))
        {
            senderBuffers.Clear();
        }

        if (_hmacs.TryRemove(steamId, out var hmac))
        {
            hmac.Dispose();
        }

        if (_ecdhInstances.TryRemove(steamId, out var ecdh))
        {
            ecdh.Dispose();
        }

        _localPublicKeys.TryRemove(steamId, out _);
        _handshakeNonces.TryRemove(steamId, out _);
        _handshakeProtocols.TryRemove(steamId, out _);

        VWorld.Log.LogDebug($"[PacketRelay] Cleaned up per-user state for {steamId}.");
    }
    public static void TryRemoveKey(ulong steamId)
    {
        CleanupUserHandshakeState(steamId);

        if (VWorld.IsClient)
        {
            _hmac?.Dispose();
            _hmac = null;
            _clientHandshakeComplete = false;
            _publicKey = [];
            _remotePublicKey = [];
            _handshakeNonce = [];
            _clientHandshakeProtocol = Const.PROTOCOL_VERSION;
            _receivedAuthenticatedServerHello = false;
        }
    }
    /// <summary>
    /// Validates the provided MAC tag against the computed tag for the unsigned payload.
    /// </summary>
    /// <param name="sender">User associated with the message.</param>
    /// <param name="unsigned">Unsigned message payload.</param>
    /// <param name="hex">Hex-encoded MAC tag.</param>
    /// <returns>True when the MAC tag matches the computed value.</returns>
    static bool VerifyMac(User sender, string unsigned, string hex)
    {
        if (!TryDecodeMacTag(hex, out byte[] receivedMac))
        {
            LogMacDiagnostic(
                $"verify rejected; reason=InvalidTagFormat platformId={GetPlatformId(sender)} typeId={GetUnsignedTypeId(unsigned)} tagLength={hex?.Length ?? 0}.");
            return false;
        }

        if (VWorld.IsClient)
        {
            byte[] mac = ComputeMacClient(unsigned);
            bool matches = mac.Length == Const.MAC_TAG_BYTES
                && CryptographicOperations.FixedTimeEquals(mac, receivedMac);
            if (!matches)
            {
                LogMacDiagnostic(
                    $"client verify rejected; reason=TagMismatch platformId={GetPlatformId(sender)} typeId={GetUnsignedTypeId(unsigned)} keyPresent={mac.Length == Const.MAC_TAG_BYTES} tagLength={receivedMac.Length}.");
            }

            return matches;
        }

        if (VWorld.IsServer)
        {
            byte[] mac = ComputeMacServer(sender, unsigned);
            bool matches = mac.Length == Const.MAC_TAG_BYTES
                && CryptographicOperations.FixedTimeEquals(mac, receivedMac);
            if (!matches)
            {
                LogMacDiagnostic(
                    $"server verify rejected; reason=TagMismatch platformId={GetPlatformId(sender)} typeId={GetUnsignedTypeId(unsigned)} keyPresent={mac.Length == Const.MAC_TAG_BYTES} tagLength={receivedMac.Length}.");
            }

            return matches;
        }

        LogMacDiagnostic(
            $"verify rejected; reason=NoRuntime platformId={GetPlatformId(sender)} typeId={GetUnsignedTypeId(unsigned)}.");
        return false;
    }
    static string GetUnsignedTypeId(string unsigned)
    {
        string[] parts = unsigned.Split('|', 4);
        return parts.Length >= 3 ? parts[2] : "Unknown";
    }
    /// <summary>
    /// Computes the truncated MAC tag for server-side validation.
    /// </summary>
    /// <param name="sender">User associated with the session.</param>
    /// <param name="input">Unsigned payload to MAC.</param>
    /// <returns>Truncated MAC tag bytes, or an empty array when unavailable.</returns>
    static byte[] ComputeMacServer(User sender, string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);

        if (!_hmacs.TryGetValue(GetPlatformId(sender), out var hmac))
        {
            return [];
        }

        byte[] hash = hmac.ComputeHash(bytes);
        return hash[..Const.MAC_TAG_BYTES];
    }
    /// <summary>
    /// Computes the truncated MAC tag for client-side validation.
    /// </summary>
    /// <param name="input">Unsigned payload to MAC.</param>
    /// <returns>Truncated MAC tag bytes, or an empty array when unavailable.</returns>
    static byte[] ComputeMacClient(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        if (_hmac == null || !_clientHandshakeComplete)
        {
            return [];
        }

        byte[] hash = _hmac.ComputeHash(bytes);
        return hash[..Const.MAC_TAG_BYTES];
    }
    /// <summary>
    /// Attempts to decode a hex-encoded MAC tag into raw bytes.
    /// </summary>
    /// <param name="hex">Hex-encoded MAC tag string.</param>
    /// <param name="tagBytes">Decoded tag bytes when successful.</param>
    /// <returns>True when the tag is a valid hex string of the expected length.</returns>
    static bool TryDecodeMacTag(string hex, out byte[] tagBytes)
    {
        tagBytes = [];

        if (string.IsNullOrWhiteSpace(hex) || hex.Length != Const.MAC_TAG_BYTES * 2)
        {
            return false;
        }

        try
        {
            tagBytes = Convert.FromHexString(hex);
            return tagBytes.Length == Const.MAC_TAG_BYTES;
        }
        catch (FormatException)
        {
            return false;
        }
    }
    static List<string> FragmentBase64(string b64)
    {
        List<string> slices = [];
        const int chars = Const.PACKET_BYTES;
        int offset = 0;

        while (offset < b64.Length)
        {
            int take = Math.Min(chars, b64.Length - offset);
            slices.Add(b64.Substring(offset, take));
            offset += take;
        }

        return slices;
    }
    static void SweepPackets()
    {
        if (_netBuffers.IsEmpty)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        foreach (var senderEntry in _netBuffers)
        {
            var senderBuffers = senderEntry.Value;
            foreach (var bufferEntry in senderBuffers)
            {
                if (now - bufferEntry.Value.LastSeen > _bufferTime)
                {
                    senderBuffers.TryRemove(bufferEntry.Key, out _);
                }
            }

            if (senderBuffers.IsEmpty)
            {
                _netBuffers.TryRemove(senderEntry.Key, out _);
            }
        }
    }
    public static bool HasPacketPrefix(string msg)
        => msg.StartsWith(Const.PREFIX, StringComparison.Ordinal);
}
