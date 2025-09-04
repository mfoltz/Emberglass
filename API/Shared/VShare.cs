using BepInEx;
using Emberglass.Network;
using Emberglass.Patches.Server;
using ProjectM;
using ProjectM.Network;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Entities;
using static Emberglass.Network.Transference;

namespace Emberglass.API.Shared;
internal static class VShare
{
    // pending refactor to client-sided trigger for receiving plugins from server folder
    static readonly string _cachePath = Path.Combine(Paths.ConfigPath, "Client");
    static readonly Regex _destinationRegex = new(@":(server|client)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    const string SERVER = ":server";
    const string CLIENT = ":client";
    const string DOWNLOAD_PREFIX = "?";
    const string HOTLOAD_PREFIX = "!";

    static readonly Dictionary<string, byte[]> _dllCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, byte[]> _zipCache = new(StringComparer.OrdinalIgnoreCase);
    public static void Initialize()
    {
        ChatMessageSystemPatch.OnChatMessageHandler += HandleShareRequest;
        Directory.CreateDirectory(_cachePath);
        BuildCache();
    }
    public static void Uninitialize()
    {
        ChatMessageSystemPatch.OnChatMessageHandler -= HandleShareRequest;
    }
    static void HandleShareRequest(Entity entity, ChatMessageEvent chatMessage, FromCharacter fromCharacter)
    {
        string messageText = chatMessage.MessageText.Value;

        bool isDownload = messageText.StartsWith(DOWNLOAD_PREFIX);
        bool isHotload = messageText.StartsWith(HOTLOAD_PREFIX);

        if (!isDownload && !isHotload)
        {
            return;
        }

        Match match = _destinationRegex.Match(messageText);
        User user = fromCharacter.User.GetUser();

        FixedString512Bytes fixedMessage;
        string message;

        if (!match.Success)
        {
            message = $"Couldn't parse destination!";
            fixedMessage = new(message);
            ServerChatUtils.SendSystemMessageToClient(VWorld.EntityManager, user, ref fixedMessage);
            return;
        }

        bool isClientbound = match.Groups[1].Value.Equals("client", StringComparison.OrdinalIgnoreCase);
        bool isServerbound = match.Groups[1].Value.Equals("server", StringComparison.OrdinalIgnoreCase);

        string destination;
        string dest;

        if (isClientbound)
        {
            destination = "Client";
            dest = CLIENT;
        }
        else if (isServerbound)
        {
            destination = "Server";
            dest = SERVER;
        }
        else
        {
            message = $"Invalid destination specified! Use '<color=white>:client</color>' or '<color=white>:server</color>' following the plugin name without spaces ('<color=white>!KinPoolParty:server</color>')";
            fixedMessage = new(message);
            ServerChatUtils.SendSystemMessageToClient(VWorld.EntityManager, user, ref fixedMessage);
            return;
        }

        string prefix = isHotload ? HOTLOAD_PREFIX : DOWNLOAD_PREFIX;
        string fileName = $"{messageText[prefix.Length..].Trim().Replace(dest, "", StringComparison.OrdinalIgnoreCase)}.dll";

        message = $"Preparing to download ~ (Plugin: <color=white>{fileName}</color> | Destination: <color=white>{destination}</color> | IsHotload: <color=white>{isHotload}</color>)";
        fixedMessage = new(message);

        InternalTransferRequest(user, new(fileName.AsSpan(), isClientbound, isHotload));
        ServerChatUtils.SendSystemMessageToClient(VWorld.EntityManager, user, ref fixedMessage);
    }
    static void BuildCache()
    {
        foreach (var file in Directory.GetFiles(_cachePath))
        {
            string ext = Path.GetExtension(file);
            string name = Path.GetFileName(file);

            byte[] raw = File.ReadAllBytes(file);
            byte[] compressed = Compress(raw);

            if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                _dllCache[name] = compressed;
            }
            else if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string pluginName = Path.GetFileNameWithoutExtension(name);
                _zipCache[pluginName] = compressed;
            }
        }
    }
    static byte[] Compress(byte[] raw)
    {
        byte[] result = null;
        var e = Transference.CompressChunkRoutine(raw, b => result = b);
        while (e.MoveNext()) { }
        return result;
    }
    public static bool TryLoadCachedDll(string fileName, out byte[] bytes)
    {
        if (_dllCache.TryGetValue(fileName, out bytes))
        {
            return true;
        }

        string path = Path.Combine(_cachePath, fileName);
        if (!File.Exists(path))
        {
            bytes = null;
            return false;
        }

        byte[] compressed = Compress(File.ReadAllBytes(path));
        _dllCache[fileName] = compressed;
        bytes = compressed;
        return true;
    }
    public static bool TryLoadCachedZip(string pluginName, out byte[] bytes)
    {
        if (_zipCache.TryGetValue(pluginName, out bytes))
        {
            return true;
        }

        string path = Path.Combine(_cachePath, pluginName + ".zip");
        if (!File.Exists(path))
        {
            bytes = null;
            return false;
        }

        byte[] compressed = Compress(File.ReadAllBytes(path));
        _zipCache[pluginName] = compressed;
        bytes = compressed;
        return true;
    }
    public static bool TryGetCachedDll(string fileName, out byte[] bytes) =>
        TryLoadCachedDll(fileName, out bytes);
    public static bool TryGetCachedZip(string pluginName, out byte[] bytes) =>
        TryLoadCachedZip(pluginName, out bytes);

    /*
    static void HandleDownloadRequest(Entity entity, ChatMessageEvent chatMessage, FromCharacter fromCharacter)
    {
        string messageText = chatMessage.MessageText.Value;

        bool isClientHotload = messageText.StartsWith(_clientHotloadPrefix);
        bool isClientDownload = messageText.StartsWith(_clientDownloadPrefix);

        bool isServerHotload = messageText.StartsWith(_serverHotloadPrefix);
        bool isServerDownload = messageText.StartsWith(_serverDownloadPrefix);

        if (!isClientHotload && !isClientDownload 
            && !isServerHotload && !isServerDownload) return;

        string prefix = string.Empty;

        switch (true)
        {
            case true when isClientHotload:
                prefix = _clientHotloadPrefix;
                break;
            case true when isClientDownload:
                prefix = _clientDownloadPrefix;
                break;
            case true when isServerHotload:
                prefix = _serverHotloadPrefix;
                break;
            case true when isServerDownload:
                prefix = _serverDownloadPrefix;
                break;
        }

        string pluginName = messageText.Replace(prefix, "").Trim();
        string fileName = $"{pluginName}.dll";

        User user = fromCharacter.User.GetUser();
        FixedString512Bytes fixedMessage = new($"Attempting to download: <color=white>{fileName}</color>");
        FileRequest fileRequest = new(fileName.AsSpan(), isClientHotload);

        FileTransfer.OnFileRequest(user, fileRequest);
        ServerChatUtils.SendSystemMessageToClient(VWorld.EntityManager, user, ref fixedMessage);
    }
    */
}