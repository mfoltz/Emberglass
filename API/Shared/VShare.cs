using BepInEx;
using Emberglass.Patches.Server;
using ProjectM;
using ProjectM.Network;
using System;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Entities;
using static Emberglass.Network.Transference;
using static Emberglass.Network.Registry;
using Emberglass.Network;

namespace Emberglass.API.Shared;
internal static class VShare
{
    // pending refactor to client-sided trigger for receiving plugins from server folder
    static readonly string _localModsPath = Path.Combine(Paths.ConfigPath, "LocalMods");
    static readonly string _serverModsPath = Path.Combine(Paths.ConfigPath, "Server");
    static readonly Regex _destinationRegex = new(@":(server|client)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    const string SERVER = ":server";
    const string CLIENT = ":client";
    const string DOWNLOAD_PREFIX = "?";
    const string HOTLOAD_PREFIX = "!";
    const string DEVELOPER_CHAT_SHARE_ENVIRONMENT_VARIABLE = "EMBERGLASS_DEV_CHAT_SHARE";

    static readonly bool developerChatShareEnabled = GetDeveloperChatShareEnabled();

    /// <summary>
    /// Gets the local mods folder used for staging DLLs and ZIPs.
    /// </summary>
    public static string LocalModsPath => _localModsPath;

    /// <summary>
    /// Gets the server mods folder used for staging clientbound DLLs and ZIPs.
    /// </summary>
    public static string ServerModsPath => _serverModsPath;

    static readonly Dictionary<string, byte[]> _dllCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, byte[]> _zipCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, byte[]> _serverDllCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, byte[]> _serverZipCache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object _cacheLock = new();
    static FileSystemWatcher _localWatcher;
    static FileSystemWatcher _serverWatcher;

    /// <summary>
    /// Initializes the share system, caches, and file watchers.
    /// </summary>
    public static void Initialize()
    {
        if (developerChatShareEnabled)
        {
            ChatMessageSystemPatch.OnChatMessageHandler += HandleShareRequest;
        }
        Directory.CreateDirectory(_localModsPath);
        Directory.CreateDirectory(_serverModsPath);
        RefreshAllCaches();
        InitializeWatchers();
    }

    /// <summary>
    /// Unregisters event handlers and disposes cache watchers.
    /// </summary>
    public static void Uninitialize()
    {
        if (developerChatShareEnabled)
        {
            ChatMessageSystemPatch.OnChatMessageHandler -= HandleShareRequest;
        }
        DisposeWatcher(_localWatcher);
        DisposeWatcher(_serverWatcher);
    }

    /// <summary>
    /// Determines whether developer-only chat transfer commands are enabled.
    /// </summary>
    /// <returns><c>true</c> when the runtime is a debug build and the opt-in flag is set; otherwise <c>false</c>.</returns>
    static bool GetDeveloperChatShareEnabled()
    {
#if DEBUG
        string flag = Environment.GetEnvironmentVariable(DEVELOPER_CHAT_SHARE_ENVIRONMENT_VARIABLE);
        return string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(flag, "yes", StringComparison.OrdinalIgnoreCase);
#else
        return false;
#endif
    }

    /// <summary>
    /// Handles chat commands requesting file transfers.
    /// </summary>
    /// <param name="entity">The chat event entity.</param>
    /// <param name="chatMessage">The incoming chat message.</param>
    /// <param name="fromCharacter">The sending character.</param>
    static void HandleShareRequest(Entity entity, ChatMessageEvent chatMessage, FromCharacter fromCharacter)
    {
        if (!developerChatShareEnabled)
        {
            return;
        }

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
        string pluginName = messageText[prefix.Length..]
            .Trim()
            .Replace(dest, "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (string.IsNullOrWhiteSpace(pluginName))
        {
            message = "Plugin name is required after removing the destination suffix.";
            fixedMessage = new(message);
            ServerChatUtils.SendSystemMessageToClient(VWorld.EntityManager, user, ref fixedMessage);
            return;
        }

        string fileName = $"{pluginName}.dll";
        int fileNameByteCount = Encoding.UTF8.GetByteCount(fileName);
        if (fileNameByteCount > Const.STANDARD_LENGTH)
        {
            message = $"Plugin name exceeds the maximum supported length of {Const.STANDARD_LENGTH} bytes.";
            fixedMessage = new(message);
            ServerChatUtils.SendSystemMessageToClient(VWorld.EntityManager, user, ref fixedMessage);
            return;
        }

        Guid offerId = SendTransferOffer(user, new(fileName.AsSpan(), isClientbound, isHotload));

        if (offerId == Guid.Empty)
        {
            message =
                $"Transfer offer skipped for <color=white>{fileName}</color>. " +
                "GitHub Release asset digest metadata could not be resolved.";
            fixedMessage = new(message);
            ServerChatUtils.SendSystemMessageToClient(VWorld.EntityManager, user, ref fixedMessage);
            return;
        }

        message =
            $"Transfer offer sent ~ (Offer: <color=white>{offerId}</color> | Plugin: <color=white>{fileName}</color> " +
            $"| Destination: <color=white>{destination}</color> | IsHotload: <color=white>{isHotload}</color>)";
        fixedMessage = new(message);

        ServerChatUtils.SendSystemMessageToClient(VWorld.EntityManager, user, ref fixedMessage);
    }

    /// <summary>
    /// Refreshes both the local and server-side caches.
    /// </summary>
    public static void RefreshAllCaches()
    {
        RefreshCache(_localModsPath, _dllCache, _zipCache);
        RefreshCache(_serverModsPath, _serverDllCache, _serverZipCache);
    }

    /// <summary>
    /// Rebuilds cached entries for a staging folder.
    /// </summary>
    /// <param name="stagingPath">The folder that contains staged mods.</param>
    /// <param name="dllCache">The DLL cache to update.</param>
    /// <param name="zipCache">The ZIP cache to update.</param>
    static void RefreshCache(string stagingPath, Dictionary<string, byte[]> dllCache, Dictionary<string, byte[]> zipCache)
    {
        Dictionary<string, byte[]> dllEntries = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, byte[]> zipEntries = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(stagingPath))
        {
            string ext = Path.GetExtension(file);
            string name = Path.GetFileName(file);

            if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadAndCompress(file, out byte[] compressed))
                {
                    dllEntries[name] = compressed;
                }
            }
            else if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string pluginName = Path.GetFileNameWithoutExtension(name);
                if (TryReadAndCompress(file, out byte[] compressed))
                {
                    zipEntries[pluginName] = compressed;
                }
            }
        }

        lock (_cacheLock)
        {
            dllCache.Clear();
            zipCache.Clear();

            foreach (var entry in dllEntries)
            {
                dllCache[entry.Key] = entry.Value;
            }

            foreach (var entry in zipEntries)
            {
                zipCache[entry.Key] = entry.Value;
            }
        }
    }

    /// <summary>
    /// Reads the file and compresses it for caching.
    /// </summary>
    /// <param name="filePath">The file path to read.</param>
    /// <param name="compressed">The compressed bytes when successful.</param>
    /// <returns><c>true</c> if the file was read and compressed; otherwise <c>false</c>.</returns>
    static bool TryReadAndCompress(string filePath, out byte[] compressed)
    {
        compressed = null;
        try
        {
            byte[] raw = File.ReadAllBytes(filePath);
            compressed = Compress(raw);
            return true;
        }
        catch (Exception ex)
        {
            VWorld.Log.LogWarning($"Failed to read cache file '{filePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Initializes file watchers that refresh caches on disk changes.
    /// </summary>
    static void InitializeWatchers()
    {
        _localWatcher = CreateCacheWatcher(_localModsPath, () => RefreshCache(_localModsPath, _dllCache, _zipCache));
        _serverWatcher = CreateCacheWatcher(_serverModsPath, () => RefreshCache(_serverModsPath, _serverDllCache, _serverZipCache));
    }

    /// <summary>
    /// Creates a file watcher for cache refresh operations.
    /// </summary>
    /// <param name="stagingPath">The directory to watch.</param>
    /// <param name="onRefresh">The callback to invoke on changes.</param>
    /// <returns>The configured file system watcher.</returns>
    static FileSystemWatcher CreateCacheWatcher(string stagingPath, Action onRefresh)
    {
        FileSystemWatcher watcher = new(stagingPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        FileSystemEventHandler handler = (_, args) =>
        {
            if (!ShouldRefreshForFile(args.FullPath))
            {
                return;
            }

            onRefresh();
        };

        RenamedEventHandler renamedHandler = (_, args) =>
        {
            if (!ShouldRefreshForFile(args.FullPath))
            {
                return;
            }

            onRefresh();
        };

        watcher.Created += handler;
        watcher.Changed += handler;
        watcher.Deleted += handler;
        watcher.Renamed += renamedHandler;

        return watcher;
    }

    /// <summary>
    /// Determines whether a change should trigger a cache refresh.
    /// </summary>
    /// <param name="filePath">The changed file path.</param>
    /// <returns><c>true</c> when the file is a DLL or ZIP; otherwise <c>false</c>.</returns>
    static bool ShouldRefreshForFile(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Disposes a watcher instance when available.
    /// </summary>
    /// <param name="watcher">The watcher to dispose.</param>
    static void DisposeWatcher(FileSystemWatcher watcher)
    {
        if (watcher is null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }

    /// <summary>
    /// Compresses the provided bytes using the transfer routine.
    /// </summary>
    /// <param name="raw">The raw bytes to compress.</param>
    /// <returns>The compressed byte array.</returns>
    static byte[] Compress(byte[] raw)
        => Transference.CompressBytesSynchronously(raw);

    /// <summary>
    /// Attempts to load or cache compressed DLL bytes.
    /// </summary>
    /// <param name="fileName">The DLL file name.</param>
    /// <param name="isClientbound">Whether the transfer is clientbound.</param>
    /// <param name="bytes">The compressed bytes when found.</param>
    /// <returns><c>true</c> when the cache is populated; otherwise <c>false</c>.</returns>
    public static bool TryLoadCachedDll(string fileName, bool isClientbound, out byte[] bytes)
    {
        var cache = GetDllCache(isClientbound);
        lock (_cacheLock)
        {
            if (cache.TryGetValue(fileName, out bytes))
            {
                return true;
            }
        }

        string path = Path.Combine(GetStagingPath(isClientbound), fileName);
        if (!File.Exists(path))
        {
            bytes = null;
            return false;
        }

        byte[] compressed = Compress(File.ReadAllBytes(path));
        lock (_cacheLock)
        {
            cache[fileName] = compressed;
        }
        bytes = compressed;
        return true;
    }

    /// <summary>
    /// Attempts to load or cache compressed ZIP bytes.
    /// </summary>
    /// <param name="pluginName">The plugin name without extension.</param>
    /// <param name="isClientbound">Whether the transfer is clientbound.</param>
    /// <param name="bytes">The compressed bytes when found.</param>
    /// <returns><c>true</c> when the cache is populated; otherwise <c>false</c>.</returns>
    public static bool TryLoadCachedZip(string pluginName, bool isClientbound, out byte[] bytes)
    {
        var cache = GetZipCache(isClientbound);
        lock (_cacheLock)
        {
            if (cache.TryGetValue(pluginName, out bytes))
            {
                return true;
            }
        }

        string path = Path.Combine(GetStagingPath(isClientbound), pluginName + ".zip");
        if (!File.Exists(path))
        {
            bytes = null;
            return false;
        }

        byte[] compressed = Compress(File.ReadAllBytes(path));
        lock (_cacheLock)
        {
            cache[pluginName] = compressed;
        }
        bytes = compressed;
        return true;
    }

    /// <summary>
    /// Attempts to read cached DLL bytes.
    /// </summary>
    /// <param name="fileName">The DLL file name.</param>
    /// <param name="isClientbound">Whether the transfer is clientbound.</param>
    /// <param name="bytes">The cached bytes when found.</param>
    /// <returns><c>true</c> when cached bytes are available; otherwise <c>false</c>.</returns>
    public static bool TryGetCachedDll(string fileName, bool isClientbound, out byte[] bytes) =>
        TryLoadCachedDll(fileName, isClientbound, out bytes);

    /// <summary>
    /// Attempts to read cached ZIP bytes.
    /// </summary>
    /// <param name="pluginName">The plugin name without extension.</param>
    /// <param name="isClientbound">Whether the transfer is clientbound.</param>
    /// <param name="bytes">The cached bytes when found.</param>
    /// <returns><c>true</c> when cached bytes are available; otherwise <c>false</c>.</returns>
    public static bool TryGetCachedZip(string pluginName, bool isClientbound, out byte[] bytes) =>
        TryLoadCachedZip(pluginName, isClientbound, out bytes);

    /// <summary>
    /// Attempts to read raw DLL bytes from the cache directory.
    /// </summary>
    /// <param name="fileName">The DLL file name.</param>
    /// <param name="isClientbound">Whether the transfer is clientbound.</param>
    /// <param name="bytes">The raw bytes when found.</param>
    /// <returns><c>true</c> when the file exists and bytes are returned; otherwise <c>false</c>.</returns>
    public static bool TryGetCachedRawDll(string fileName, bool isClientbound, out byte[] bytes)
    {
        string path = Path.Combine(GetStagingPath(isClientbound), fileName);
        if (!File.Exists(path))
        {
            bytes = null;
            return false;
        }

        bytes = File.ReadAllBytes(path);
        return true;
    }

    /// <summary>
    /// Attempts to read raw ZIP bytes from the cache directory.
    /// </summary>
    /// <param name="pluginName">The plugin name without extension.</param>
    /// <param name="isClientbound">Whether the transfer is clientbound.</param>
    /// <param name="bytes">The raw bytes when found.</param>
    /// <returns><c>true</c> when the file exists and bytes are returned; otherwise <c>false</c>.</returns>
    public static bool TryGetCachedRawZip(string pluginName, bool isClientbound, out byte[] bytes)
    {
        string path = Path.Combine(GetStagingPath(isClientbound), $"{pluginName}.zip");
        if (!File.Exists(path))
        {
            bytes = null;
            return false;
        }

        bytes = File.ReadAllBytes(path);
        return true;
    }

    /// <summary>
    /// Invalidates a cached plugin entry by removing in-memory data and deleting the on-disk file.
    /// </summary>
    /// <param name="fileName">The full file name of the cached entry.</param>
    /// <param name="pluginName">The plugin name without extension.</param>
    /// <param name="isZip">Whether the cached entry is a ZIP package.</param>
    /// <param name="isClientbound">Whether the cache entry is in the server staging folder.</param>
    /// <param name="errorMessage">An error message when invalidation fails.</param>
    /// <returns><c>true</c> when invalidation succeeds; otherwise <c>false</c>.</returns>
    public static bool TryInvalidateCachedEntry(
        string fileName,
        string pluginName,
        bool isZip,
        bool isClientbound,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (isZip)
        {
            RemoveCacheEntry(GetZipCache(isClientbound), pluginName);
        }
        else
        {
            RemoveCacheEntry(GetDllCache(isClientbound), fileName);
        }

        string cacheFile = isZip ? $"{pluginName}.zip" : fileName;
        string path = Path.Combine(GetStagingPath(isClientbound), cacheFile);

        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to delete cached file '{path}': {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Removes a cache entry under a lock.
    /// </summary>
    /// <param name="cache">The cache dictionary.</param>
    /// <param name="key">The cache key to remove.</param>
    static void RemoveCacheEntry(Dictionary<string, byte[]> cache, string key)
    {
        lock (_cacheLock)
        {
            cache.Remove(key);
        }
    }

    /// <summary>
    /// Selects the DLL cache for the transfer direction.
    /// </summary>
    /// <param name="isClientbound">Whether the transfer is clientbound.</param>
    /// <returns>The appropriate DLL cache dictionary.</returns>
    static Dictionary<string, byte[]> GetDllCache(bool isClientbound) =>
        isClientbound ? _serverDllCache : _dllCache;

    /// <summary>
    /// Selects the ZIP cache for the transfer direction.
    /// </summary>
    /// <param name="isClientbound">Whether the transfer is clientbound.</param>
    /// <returns>The appropriate ZIP cache dictionary.</returns>
    static Dictionary<string, byte[]> GetZipCache(bool isClientbound) =>
        isClientbound ? _serverZipCache : _zipCache;

    /// <summary>
    /// Selects the staging path for the transfer direction.
    /// </summary>
    /// <param name="isClientbound">Whether the transfer is clientbound.</param>
    /// <returns>The staging folder path.</returns>
    static string GetStagingPath(bool isClientbound) =>
        isClientbound ? _serverModsPath : _localModsPath;

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
