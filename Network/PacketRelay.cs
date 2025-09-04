using Emberglass.API.Shared;
using ProjectM.Network;
using System.Collections.Concurrent;
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

    static readonly ConcurrentDictionary<string, NetBuffer> _netBuffers = [];
    static readonly TimeSpan _bufferTime = TimeSpan.FromSeconds(LIFETIME);

    static readonly HMACSHA256 _shared = new(Encoding.UTF8.GetBytes(Const.SHARED_KEY));
    static readonly ConcurrentDictionary<ulong, HMACSHA256> _hmacs = [];

    static readonly ConcurrentDictionary<ulong, byte[]> _localPublicKeys = [];
    static byte[] _publicKey = [];
    static byte[] _localKey = [];

    static HMACSHA256 _hmac;
    static readonly ConcurrentDictionary<ulong, ECDiffieHellman> _ecdhInstances = [];

    static int _nextMsgId = 1;
    const int LIFETIME = 100; // ~2MB share size limit

    static bool _initialized = false;
    unsafe struct KeyExchange
    {
        public fixed byte AsymmetricKey[Const.EXCHANGE_LENGTH];
        public KeyExchange(ReadOnlySpan<byte> key)
        {
            fixed (byte* dest = AsymmetricKey)
            {
                int bytesToCopy = Math.Min(key.Length, Const.EXCHANGE_LENGTH);
                for (int i = 0; i < bytesToCopy; i++)
                {
                    dest[i] = key[i];
                }

                for (int i = bytesToCopy; i < Const.EXCHANGE_LENGTH; i++)
                {
                    dest[i] = 0;
                }
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
        public byte[] KeyBytes => KeySpan.ToArray();
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
            ModuleRegistry.Subscribe<UserConnected>(OnUserConnected);
        }

        if (VWorld.IsClient)
        {
            ModuleRegistry.Subscribe<ClientHandshake>(OnClientReady);
        }

        Register<ClientHandshake>(Direction.Serverbound, (u, o) => OnClientHandshake(u));
        Register<KeyExchange>(Direction.Clientbound, (u, o) => OnKeyExchange(u, (KeyExchange)o));
        Register<KeyExchange>(Direction.Serverbound, (u, o) => OnKeyExchange(u, (KeyExchange)o));

        _initialized = true;
    }
    static void OnUserConnected(UserConnected connected)
    {
        ulong id = connected.PlayerInfo.SteamId;

        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _ecdhInstances[id] = ecdh;
        _localPublicKeys[id] = ecdh.ExportSubjectPublicKeyInfo();
    }
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

        string hex = payload[(lastPipe + 1)..];
        string unsigned = payload[..lastPipe];
        // if (!VerifyMac(unsigned, tagHex)) return;           
        if (!VerifyMac(sender, unsigned, hex))
        {
            return;
        }

        string[] parts = unsigned.Split('|', 4);
        if (parts.Length < 4)
        {
            return;
        }

        string msgGuid = parts[0];
        string partInfo = parts[1];
        string typeIdStr = parts[2];
        string thisChunk = parts[3];

        var tuple = partInfo.Split('/');
        int idx = int.Parse(tuple[0]);
        int total = int.Parse(tuple[1]);

        NetBuffer buffer = _netBuffers.GetOrAdd(msgGuid, _ => new(total));
        if (buffer.AddPart(idx, thisChunk))
        {
            _netBuffers.TryRemove(msgGuid, out _);
            UnpackPacket(
                sender,
                uint.Parse(typeIdStr),
                buffer.Concat());
        }

        packet.Destroy(true);
        SweepPackets();
    }
    static void UnpackPacket(User sender, uint typeId, string b64)
    {
        if (!TryGet(typeId, out Handler handler) || handler == null)
        {
            return;
        }

        bool shouldUnpack = handler.Dir switch
        {
            Direction.Serverbound => VWorld.IsServer,
            Direction.Clientbound => VWorld.IsClient,
            _ => false
        };

        if (!shouldUnpack)
        {
            return;
        }

        object obj = handler.Unpack(Convert.FromBase64String(b64));
        handler.Invoke(sender, obj);
    }
    public static void SendPacketFromServer<T>(User user, T packet) where T : struct
    {
        Type type = packet.GetType();
        uint typeId = Hash32(type.FullName!);
        var pack = Serialization.GetPacker(type);
        byte[] data = pack(packet);
        string b64 = Convert.ToBase64String(data);

        string msgGuid = Interlocked.Increment(ref _nextMsgId).ToString("X6");
        var slices = FragmentBase64(b64);
        int total = slices.Count;

        for (int i = 0; i < total; i++)
        {
            string header = $"{msgGuid}|{i}/{total}|{typeId}|";
            string preHmac = $"{header}{slices[i]}";
            string tag = ComputeMacServer(user, preHmac);

            _sendServerPacket(user, $"{Const.PREFIX}{preHmac}|{tag}");
        }
    }
    public static void SendPacketFromClient<T>(User user, T packet) where T : unmanaged
    {
        Type type = typeof(T);
        uint typeId = Hash32(type.FullName!);
        var pack = Serialization.GetPacker(type);
        byte[] data = pack(packet!);
        string b64 = Convert.ToBase64String(data);

        string msgGuid = Interlocked.Increment(ref _nextMsgId).ToString("X6");
        var slices = FragmentBase64(b64);
        int total = slices.Count;

        for (int i = 0; i < total; i++)
        {
            string header = $"{msgGuid}|{i}/{total}|{typeId}|";
            string unsigned = $"{header}{slices[i]}";
            string tag = ComputeMacClient(unsigned);

            _sendClientPacket(user, $"{Const.PREFIX}{unsigned}|{tag}");
        }
    }
    static void OnClientReady(ClientHandshake handshake)
        => VNetwork.SendToServer(handshake);
    static void OnClientHandshake(User user)
        => VNetwork.SendToClient(user, new KeyExchange(_publicKey));
    static void OnKeyExchange(User user, KeyExchange exchange)
    {
        if (!_ecdhInstances.TryGetValue(user.PlatformId, out var localEcdh))
        {
            localEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

            _ecdhInstances[user.PlatformId] = localEcdh;

            byte[] localPublic = localEcdh.ExportSubjectPublicKeyInfo();
            if (VWorld.IsServer)
            {
                _localPublicKeys[user.PlatformId] = localPublic;
            }
            else
            {
                _publicKey = localPublic;
            }
        }

        using var remotePublic = ECDiffieHellman.Create();
        remotePublic.ImportSubjectPublicKeyInfo(exchange.KeySpan, out _);
        byte[] shared = localEcdh.DeriveKeyMaterial(remotePublic.PublicKey);

        if (VWorld.IsClient)
        {
            VNetwork.SendToServer(new KeyExchange(_publicKey));
        }

        SetKey(user, shared);
    }
    static void SetKey(User sender, byte[] key)
    {
        if (VWorld.IsServer)
        {
            var hmac = new HMACSHA256(key);
            _hmacs.AddOrUpdate(sender.PlatformId, hmac, (_, old) =>
            {
                if (!ReferenceEquals(old, _shared))
                {
                    old.Dispose();
                }

                return hmac;
            });
        }

        if (VWorld.IsClient)
        {
            _hmac = new HMACSHA256(key);
        }
    }
    public static void TryRemoveKey(ulong steamId)
    {
        if (_hmacs.TryRemove(steamId, out var hmac))
        {
            hmac.Dispose();
        }

        if (_ecdhInstances.TryRemove(steamId, out var ecdh))
        {
            ecdh.Dispose();
        }

        _localPublicKeys.TryRemove(steamId, out _);
    }
    static bool VerifyMac(User sender, string unsigned, string hex)
    {
        if (VWorld.IsClient)
        {
            return string.Equals(ComputeMacServer(sender, unsigned), hex, StringComparison.OrdinalIgnoreCase);
        }

        if (VWorld.IsServer)
        {
            return string.Equals(ComputeMacClient(unsigned), hex, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
    static string ComputeMacServer(User sender, string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);

        if (!_hmacs.TryGetValue(sender.PlatformId, out var hmac))
        {
            hmac = _shared;
        }

        byte[] hash = hmac.ComputeHash(bytes);
        return BitConverter.ToString(hash, 0, 8).Replace("-", "");
    }
    static string ComputeMacClient(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        HMACSHA256 hmac = _shared;

        if (_hmac != null)
        {
            hmac = _hmac;
        }

        byte[] hash = hmac.ComputeHash(bytes);
        return BitConverter.ToString(hash, 0, 8).Replace("-", "");
    }
    static List<string> FragmentBase64(string b64)
    {
        List<string> slices = [];
        int chars = Const.REMAINING_BYTES;
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

        foreach (var kv in _netBuffers)
        {
            if (now - kv.Value.LastSeen > _bufferTime)
            {
                _netBuffers.TryRemove(kv.Key, out _);
            }
        }
    }
    public static bool HasPacketPrefix(string msg)
        => msg.StartsWith(Const.PREFIX, StringComparison.Ordinal);
}
