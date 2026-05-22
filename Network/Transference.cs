using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Emberglass.API.Client;
using Emberglass.API.Shared;
using Emberglass.Utilities;
using ProjectM;
using ProjectM.Network;
using Unity.Collections;
using UnityEngine;
using static Emberglass.API.Server.ServerModules.ConnectionModules;
using static Emberglass.API.Shared.VEvents;
using static Emberglass.Network.Registry;

namespace Emberglass.Network;
internal enum HotloadPluginStatus
{
    Loaded,
    FileMissing,
    PluginTypeMissing,
    PluginCreateFailed,
    PluginGuidAlreadyLoaded,
    Failed
}

internal readonly record struct HotloadPluginResult(
    bool Success,
    HotloadPluginStatus Status,
    string Message,
    string PluginGuid = null,
    string PluginName = null,
    string PluginVersion = null,
    string AssemblyName = null);

internal static class Transference
{
    public unsafe struct TransferRequest
    {
        public readonly bool Clientbound;
        public readonly bool Hotload;

        public fixed byte FileName[Const.STANDARD_LENGTH];
        public TransferRequest(ReadOnlySpan<char> fileName, bool clientbound, bool hotload)
        {
            fixed (byte* dest = FileName)
            {
                int bytesWritten = System.Text.Encoding.UTF8.GetBytes(fileName, new Span<byte>(dest, Const.STANDARD_LENGTH));
                for (int i = bytesWritten; i < Const.STANDARD_LENGTH; i++)
                {
                    dest[i] = 0;
                }
            }

            Clientbound = clientbound;
            Hotload = hotload;
        }
        public string FileNameString
        {
            get
            {
                fixed (byte* p = FileName)
                {
                    int len = 0;
                    while (len < Const.STANDARD_LENGTH && p[len] != 0)
                    {
                        len++;
                    }

                    return System.Text.Encoding.UTF8.GetString(p, len);
                }
            }
        }
    }

    /// <summary>
    /// Represents the client-side mod metadata sent to the server when requesting shared mods.
    /// </summary>
    public readonly struct ClientModSignature
    {
        public readonly string FileName;
        public readonly string Sha256;

        /// <summary>
        /// Initializes a new client mod signature entry.
        /// </summary>
        /// <param name="fileName">The DLL file name on the client.</param>
        /// <param name="sha256">The SHA-256 hash for the DLL, encoded as hex.</param>
        public ClientModSignature(string fileName, string sha256)
        {
            FileName = fileName;
            Sha256 = sha256;
        }
    }

    /// <summary>
    /// Requests the server to offer shared mods that are missing on the client.
    /// </summary>
    public readonly struct RequestSharedClientMods
    {
        public readonly ClientModSignature[] ClientMods;

        /// <summary>
        /// Initializes a new request containing the client's installed mod list.
        /// </summary>
        /// <param name="clientMods">The client mod signatures to compare against.</param>
        public RequestSharedClientMods(ClientModSignature[] clientMods)
        {
            ClientMods = clientMods ?? [];
        }
    }

    /// <summary>
    /// Describes the server-side shared mods currently advertised to a client.
    /// </summary>
    public sealed class SharedClientModPreview
    {
        public SharedClientModPreviewEntry[] Entries { get; set; } = [];

        public SharedClientModPreview()
        {
        }

        /// <summary>
        /// Initializes a new preview payload for the client options menu.
        /// </summary>
        /// <param name="entries">The advertised shared-mod entries.</param>
        public SharedClientModPreview(SharedClientModPreviewEntry[] entries)
        {
            Entries = entries ?? [];
        }
    }

    /// <summary>
    /// Describes one shared mod shown in the client options-menu details.
    /// </summary>
    public sealed class SharedClientModPreviewEntry
    {
        public string DisplayName { get; set; } = string.Empty;
        public string GitHubRepo { get; set; } = string.Empty;
        public string GitHubTag { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public bool IsZip { get; set; }
        public bool Hotload { get; set; }

        public SharedClientModPreviewEntry()
        {
        }

        /// <summary>
        /// Initializes a new shared-mod preview entry.
        /// </summary>
        /// <param name="displayName">The human-readable mod name.</param>
        /// <param name="gitHubRepo">The GitHub repository identity.</param>
        /// <param name="gitHubTag">The GitHub release tag.</param>
        /// <param name="isZip">Whether the staged asset is a ZIP package.</param>
        /// <param name="hotload">Whether runtime load is enabled for this offer.</param>
        public SharedClientModPreviewEntry(
            string displayName,
            string gitHubRepo,
            string gitHubTag,
            bool isZip,
            bool hotload)
            : this(displayName, gitHubRepo, gitHubTag, string.Empty, isZip, hotload)
        {
        }

        /// <summary>
        /// Initializes a new shared-mod preview entry.
        /// </summary>
        /// <param name="displayName">The human-readable mod name.</param>
        /// <param name="gitHubRepo">The GitHub repository identity.</param>
        /// <param name="gitHubTag">The GitHub release tag.</param>
        /// <param name="fileName">The staged asset file name.</param>
        /// <param name="isZip">Whether the staged asset is a ZIP package.</param>
        /// <param name="hotload">Whether runtime load is enabled for this offer.</param>
        public SharedClientModPreviewEntry(
            string displayName,
            string gitHubRepo,
            string gitHubTag,
            string fileName,
            bool isZip,
            bool hotload)
        {
            DisplayName = displayName ?? string.Empty;
            GitHubRepo = gitHubRepo ?? string.Empty;
            GitHubTag = gitHubTag ?? string.Empty;
            FileName = fileName ?? string.Empty;
            IsZip = isZip;
            Hotload = hotload;
        }
    }

    public unsafe struct TransferOffer
    {
        public readonly Guid Id;
        public readonly bool Clientbound;
        public readonly bool Hotload;

        public fixed byte FileName[Const.STANDARD_LENGTH];
        public TransferOffer(Guid id, ReadOnlySpan<char> fileName, bool clientbound, bool hotload)
        {
            Id = id;
            Clientbound = clientbound;
            Hotload = hotload;

            fixed (byte* dest = FileName)
            {
                int bytesWritten = System.Text.Encoding.UTF8.GetBytes(fileName, new Span<byte>(dest, Const.STANDARD_LENGTH));
                for (int i = bytesWritten; i < Const.STANDARD_LENGTH; i++)
                {
                    dest[i] = 0;
                }
            }
        }
        public string FileNameString
        {
            get
            {
                fixed (byte* p = FileName)
                {
                    int len = 0;
                    while (len < Const.STANDARD_LENGTH && p[len] != 0)
                    {
                        len++;
                    }

                    return System.Text.Encoding.UTF8.GetString(p, len);
                }
            }
        }
    }

    public readonly struct TransferAccept(Guid id)
    {
        public readonly Guid Id = id;
    }

    public readonly struct TransferDecline(Guid id, TransferDeclineReason reason)
    {
        public readonly Guid Id = id;
        public readonly TransferDeclineReason Reason = reason;
    }

    public enum TransferDeclineReason : byte
    {
        UserDeclined = 0,
        OfferExpired = 1,
        UserDisconnected = 2,
        Unknown = 3
    }
    public unsafe struct TransferSession
    {
        public readonly Guid Id;
        public readonly int TotalBytes;

        public fixed byte FileName[Const.STANDARD_LENGTH];
        public fixed byte Sha256[Const.STANDARD_LENGTH];
        public fixed byte ReleaseAssetSha256[Const.STANDARD_LENGTH];
        public TransferSession(
            Guid id,
            int totalBytes,
            ReadOnlySpan<char> fileName,
            ReadOnlySpan<byte> sha256,
            ReadOnlySpan<byte> releaseAssetSha256)
        {
            Id = id;
            TotalBytes = totalBytes;

            fixed (byte* dest = FileName)
            {
                int bytesWritten = System.Text.Encoding.UTF8.GetBytes(fileName, new Span<byte>(dest, Const.STANDARD_LENGTH));
                for (int i = bytesWritten; i < Const.STANDARD_LENGTH; i++)
                {
                    dest[i] = 0;
                }
            }

            fixed (byte* dest = Sha256)
            {
                int bytesToCopy = Math.Min(sha256.Length, Const.STANDARD_LENGTH);
                for (int i = 0; i < bytesToCopy; i++)
                {
                    dest[i] = sha256[i];
                }

                for (int i = bytesToCopy; i < Const.STANDARD_LENGTH; i++)
                {
                    dest[i] = 0;
                }
            }

            fixed (byte* dest = ReleaseAssetSha256)
            {
                int bytesToCopy = Math.Min(releaseAssetSha256.Length, Const.STANDARD_LENGTH);
                for (int i = 0; i < bytesToCopy; i++)
                {
                    dest[i] = releaseAssetSha256[i];
                }

                for (int i = bytesToCopy; i < Const.STANDARD_LENGTH; i++)
                {
                    dest[i] = 0;
                }
            }
        }
        public string FileNameString
        {
            get
            {
                fixed (byte* p = FileName)
                {
                    int len = 0;
                    while (len < Const.STANDARD_LENGTH && p[len] != 0)
                    {
                        len++;
                    }

                    return System.Text.Encoding.UTF8.GetString(p, len);
                }
            }
        }
        public ReadOnlySpan<byte> Sha256Span
        {
            get
            {
                fixed (byte* ptr = Sha256)
                {
                    return new ReadOnlySpan<byte>(ptr, Const.STANDARD_LENGTH);
                }
            }
        }
        public byte[] Sha256Bytes => Sha256Span.ToArray();
        public ReadOnlySpan<byte> ReleaseAssetSha256Span
        {
            get
            {
                fixed (byte* ptr = ReleaseAssetSha256)
                {
                    return new ReadOnlySpan<byte>(ptr, Const.STANDARD_LENGTH);
                }
            }
        }
        public byte[] ReleaseAssetSha256Bytes => ReleaseAssetSha256Span.ToArray();
    }
    public readonly struct TransferComplete(Guid id, bool hotload)
    {
        public readonly Guid Id = id;
        public readonly bool Hotload = hotload;
    }
    readonly record struct QueuedOutgoingTransfer(Guid TransferId, User Target, TransferRequest Request) : IOutgoingTransferWork
    {
        public ulong TargetId => Target.PlatformId;
    }
    public unsafe struct TransferChunk
    {
        public readonly Guid Id;
        public readonly int Index;
        public readonly ushort Length;

        public fixed byte Packet[Const.PACKET_BYTES];
        public TransferChunk(Guid id, int index, byte[] chunk)
        {
            if (chunk.Length > Const.PACKET_BYTES)
            {
                throw new ArgumentException($"Payload too large: {chunk.Length} > {Const.PACKET_BYTES}", nameof(chunk));
            }

            Id = id;
            Index = index;
            Length = (ushort)chunk.Length;

            for (int i = 0; i < Length; i++)
            {
                Packet[i] = chunk[i];
            }

            for (int i = Length; i < Const.PACKET_BYTES; i++)
            {
                Packet[i] = 0;
            }
        }
        public ReadOnlySpan<byte> ChunkSpan
        {
            get
            {
                fixed (byte* ptr = Packet)
                {
                    return new ReadOnlySpan<byte>(ptr, Length);
                }
            }
        }
        public byte[] ChunkBytes => ChunkSpan.ToArray();
    }

    static readonly Dictionary<Guid, IncomingTransfer> incomingTransfers = [];
    static readonly object incomingLock = new();
    static readonly Dictionary<Guid, PendingClientOffer> pendingClientOffers = [];
    static readonly Dictionary<Guid, PendingServerOffer> pendingServerOffers = [];
    static readonly object pendingOfferLock = new();
    static readonly Dictionary<OverwriteConfirmationKey, DateTime> overwriteConfirmations = [];
    static readonly object overwriteConfirmationLock = new();
    static DateTime sharedModRequestConsentExpiresAtUtc = DateTime.MinValue;
    static long sharedModRequestConsentClientSessionGeneration = -1;
    static bool offerCleanupStarted;
    static bool incomingCleanupStarted;
    static bool transferWorkQueueStarted;
    static bool _initialized;
    static readonly object outgoingTransferLock = new();

    static readonly WaitForSeconds offerCleanupDelay = new(2f);
    static readonly WaitForSeconds incomingCleanupDelay = new(2f);
    static readonly PluginShareMetadataStore _shareMetadataStore = new();
    static readonly GitHubReleaseClient _gitHubReleaseClient = new();
    static readonly TransferWorkQueue transferWorkQueue = new();
    static readonly OutgoingTransferScheduler<QueuedOutgoingTransfer> outgoingTransferScheduler = new(DEFAULT_MAX_ACTIVE_OUTGOING_TRANSFERS);
    static readonly Dictionary<ReleaseAssetCacheKey, ReleaseAssetCacheEntry> releaseAssetDigestCache = [];
    static readonly object releaseAssetDigestCacheLock = new();
    static readonly HashSet<string> _hotloadedPluginGuids = new(StringComparer.Ordinal);
    static SharedClientModPreviewEntry[] _latestSharedClientModPreview;
    static readonly HashSet<string> _sharedModAutoAcceptFileNames = new(StringComparer.OrdinalIgnoreCase);
    static DateTime _sharedModAutoAcceptUntilUtc = DateTime.MinValue;
    const string CLIENT_TAG = "client";
    const string EMBERGLASS_PLUGIN_NAME = "Emberglass";
    const string STAGED_ASSET_DOUBLE_DELIMITER = "__";
    const char STAGED_ASSET_SINGLE_DELIMITER = '_';
    const int OFFER_TIMEOUT_SECONDS = 60;
    const int DEFAULT_TRANSFER_WORK_BUDGET_MS = 2;
    const int DEFAULT_MAX_TRANSFER_WORK_STEPS_PER_FRAME = 8;
    const int DEFAULT_MAX_ACTIVE_OUTGOING_TRANSFERS = 2;
    const int RELEASE_DIGEST_CACHE_TTL_MINUTES = 15;
    const int TRANSFER_WORK_QUEUE_LOG_INTERVAL_SECONDS = 5;
    const int TRANSFER_PROGRESS_LOG_PERCENT_STEP = 25;
    const int TRANSFER_PROGRESS_BAR_WIDTH = 10;
    const int MAX_SHARED_MOD_PREVIEW_ENTRIES = 8;
    const int SHARED_MOD_AUTO_ACCEPT_WINDOW_SECONDS = 15;
    const string REQUEST_SHARED_MODS_BUTTON_ID = "emberglass.request_shared_mods";
    const string SHARED_MODS_MENU_HEADER = "Server shared mods:";
    delegate bool TryGetShareMetadataDelegate(
        string metadataKey,
        out PluginShareMetadataStore.PluginShareMetadata metadata,
        out string errorMessage);
    delegate bool TryGetClientShareMetadataDelegate(
        SharedModEntry entry,
        out PluginShareMetadataStore.PluginShareMetadata metadata,
        out string skipReason);
    /// <summary>
    /// Gets or sets the per-frame time budget, in milliseconds, for processing transfer work items.
    /// </summary>
    /// <remarks>
    /// Keep this budget well below <see cref="Const.CHUNK_DELAY" /> (converted to milliseconds) so
    /// transfer work yields between network chunk sends and avoids main-thread stalls.
    /// </remarks>
    internal static int TransferWorkQueueBudgetMs { get; set; } = DEFAULT_TRANSFER_WORK_BUDGET_MS;
    /// <summary>
    /// Gets or sets the maximum transfer work steps processed per frame.
    /// </summary>
    internal static int MaxTransferWorkStepsPerFrame { get; set; } = DEFAULT_MAX_TRANSFER_WORK_STEPS_PER_FRAME;
    /// <summary>
    /// Gets or sets the maximum number of outgoing transfers that may actively send at once.
    /// </summary>
    internal static int MaxActiveOutgoingTransfers { get; set; } = DEFAULT_MAX_ACTIVE_OUTGOING_TRANSFERS;
    /// <summary>
    /// Defines how long an incoming transfer can remain idle before it is pruned.
    /// Keep this comfortably above <see cref="Const.CHUNK_DELAY" /> so slow networks or
    /// tuned chunk pacing do not cause active transfers to be swept prematurely.
    /// </summary>
    const int INCOMING_TRANSFER_TIMEOUT_SECONDS = 120;
    const int MAX_COLLISION_LOG_COUNT = 5;
    static readonly TimeSpan offerTimeout = TimeSpan.FromSeconds(OFFER_TIMEOUT_SECONDS);
    static readonly TimeSpan incomingTransferTimeout = TimeSpan.FromSeconds(INCOMING_TRANSFER_TIMEOUT_SECONDS);
    static DateTime lastTransferWorkQueueLogUtc = DateTime.MinValue;
    /// <summary>
    /// Gets or sets the provider used to resolve the current UTC time.
    /// </summary>
    /// <remarks>
    /// Override this provider in tests to control time-dependent behaviors.
    /// </remarks>
    internal static Func<DateTime> UtcNowProvider { get; set; } = () => DateTime.UtcNow;
    /// <summary>
    /// Gets the configured timeout for incoming transfers.
    /// </summary>
    /// <returns>The timeout duration for idle incoming transfers.</returns>
    internal static TimeSpan IncomingTransferTimeout => incomingTransferTimeout;
    /// <summary>
    /// Applies the current VShare transfer throttling settings.
    /// </summary>
    /// <param name="settings">The settings snapshot to apply.</param>
    internal static void ConfigureTransferSettings(VShareTransferSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        TransferWorkQueueBudgetMs = settings.TransferWorkBudgetMs;
        MaxTransferWorkStepsPerFrame = settings.MaxTransferWorkStepsPerFrame;
        MaxActiveOutgoingTransfers = settings.MaxActiveOutgoingTransfers;

        lock (outgoingTransferLock)
        {
            outgoingTransferScheduler.SetMaxActiveTransfers(MaxActiveOutgoingTransfers);
        }
    }
    /// <summary>
    /// Occurs when an incoming transfer exceeds the idle timeout during cleanup.
    /// </summary>
    internal static event Action<IncomingTransfer, TimeSpan> IncomingTransferTimedOut;
    /// <summary>
    /// Returns the current UTC time from the configured provider.
    /// </summary>
    /// <returns>The current UTC timestamp.</returns>
    static DateTime GetUtcNow()
    {
        return UtcNowProvider?.Invoke() ?? DateTime.UtcNow;
    }
    public static void Bootstrap()
    {
        if (_initialized)
        {
            return;
        }

        // Register<TransferRequest>(Direction.Serverbound, (u, o) => OnTransferRequest(u, (TransferRequest)o));
        // Register<FileAck>(Direction.Serverbound, (u, o) => OnFileAck((FileAck)o));

        Register<TransferOffer>(Direction.Clientbound, (u, o) => OnTransferOffer((TransferOffer)o));
        Register<TransferAccept>(Direction.Serverbound, (u, o) => OnTransferAccept(u, (TransferAccept)o));
        Register<TransferDecline>(Direction.Serverbound, (u, o) => OnTransferDecline(u, (TransferDecline)o));
        Register<TransferSession>(Direction.Clientbound, (u, o) => OnTransferSession(u, (TransferSession)o));
        Register<TransferComplete>(Direction.Clientbound, (u, o) => OnTransferComplete((TransferComplete)o));
        Register<TransferChunk>(Direction.Clientbound, (u, o) => OnTransferChunk((TransferChunk)o));
        Register<RequestSharedClientMods>(Direction.Serverbound, (u, o) => HandleSharedModsRequest(u, (RequestSharedClientMods)o));
        Register<SharedClientModPreview>(Direction.Clientbound, (u, o) => OnSharedClientModPreview((SharedClientModPreview)o));

        if (VWorld.IsServer)
        {
            ModuleRegistry.Subscribe<UserDisconnected>(OnUserDisconnected);
            VNetwork.OnReady += OnVNetworkReady;
        }
        else if (VWorld.IsClient)
        {
            API.Shared.VNetwork.OnClientSessionReset += ClearSharedModRequestConsent;
        }

        StartOfferCleanup();
        StartIncomingTransferCleanup();
        StartTransferWorkQueue();

        _initialized = true;
    }

    /// <summary>
    /// Sends a transfer offer to the target user and tracks it until accepted or declined.
    /// </summary>
    /// <param name="user">The target user who must approve the transfer.</param>
    /// <param name="request">The requested transfer details.</param>
    /// <returns>The identifier for the newly created transfer offer.</returns>
    public static Guid SendTransferOffer(User user, TransferRequest request)
    {
        if (VWorld.IsServer && request.Clientbound)
        {
            if (!TryResolveShareDigestForOffer(request.FileNameString, out string resolveError))
            {
                VWorld.Log.LogWarning(
                    $"Transfer offer skipped for {request.FileNameString}: {resolveError}");
                return Guid.Empty;
            }
        }

        Guid offerId = Guid.NewGuid();
        var offer = new TransferOffer(offerId, request.FileNameString, request.Clientbound, request.Hotload);
        DateTime expiresAt = GetUtcNow().Add(offerTimeout);

        lock (pendingOfferLock)
        {
            pendingServerOffers[offerId] = new PendingServerOffer(offer, request, user.PlatformId, expiresAt);
        }

        VWorld.Log.LogInfo(
            $"[VShare] Offer sent: {offer.FileNameString} -> {user.PlatformId} (offer {offerId}, expires {expiresAt:HH\\:mm\\:ss}).");

        API.Shared.VNetwork.SendToClient(user, offer);
        return offerId;
    }

    /// <summary>
    /// Gets the current pending transfer offers on the client.
    /// </summary>
    /// <returns>The collection of pending offers.</returns>
    public static IReadOnlyCollection<TransferOffer> GetPendingTransferOffers()
    {
        if (!VWorld.IsClient)
        {
            return Array.Empty<TransferOffer>();
        }

        CleanupExpiredOffers();

        lock (pendingOfferLock)
        {
            return pendingClientOffers.Values.Select(offer => offer.Offer).ToArray();
        }
    }

    /// <summary>
    /// Accepts a pending transfer offer on the client.
    /// </summary>
    /// <param name="offerId">The offer identifier to accept.</param>
    /// <param name="allowOverwrite">
    /// Whether the caller explicitly approves overwriting existing mods for this offer.
    /// </param>
    /// <returns><c>true</c> if the offer was accepted; otherwise, <c>false</c>.</returns>
    public static bool TryAcceptTransferOffer(Guid offerId, bool allowOverwrite = false)
    {
        if (!VWorld.IsClient)
        {
            VWorld.Log.LogWarning("Transfer offers can only be accepted on a client.");
            return false;
        }

        if (!TryRemoveClientOffer(offerId, out TransferOffer offer))
        {
            return false;
        }

        if (allowOverwrite)
        {
            RegisterOverwriteConfirmation(offer.Id, offer.FileNameString);
            VWorld.Log.LogWarning(
                $"Overwrite confirmation recorded for {offer.FileNameString} (offer {offer.Id}).");
        }

        VWorld.Log.LogInfo($"[VShare] Offer accepted: {offer.FileNameString} (offer {offer.Id}).");
        API.Shared.VNetwork.SendToServer(new TransferAccept(offerId));
        return true;
    }

    /// <summary>
    /// Declines a pending transfer offer on the client.
    /// </summary>
    /// <param name="offerId">The offer identifier to decline.</param>
    /// <param name="reason">The reason for declining the offer.</param>
    /// <returns><c>true</c> if the offer was declined; otherwise, <c>false</c>.</returns>
    public static bool TryDeclineTransferOffer(Guid offerId, TransferDeclineReason reason = TransferDeclineReason.UserDeclined)
    {
        if (!VWorld.IsClient)
        {
            VWorld.Log.LogWarning("Transfer offers can only be declined on a client.");
            return false;
        }

        if (!TryRemoveClientOffer(offerId, out TransferOffer offer))
        {
            return false;
        }

        VWorld.Log.LogWarning(
            $"Transfer declined ~ ID: {offer.Id} | Plugin: {offer.FileNameString} | Reason: {reason}");
        API.Shared.VNetwork.SendToServer(new TransferDecline(offerId, reason));
        return true;
    }

    public static void InternalTransferRequest(User user, TransferRequest request)
    {
        InternalTransferRequest(user, request, null);
    }

    static void InternalTransferRequest(User user, TransferRequest request, Guid? transferId)
    {
        Guid id = transferId ?? Guid.NewGuid();
        if (VWorld.IsServer && request.Clientbound)
        {
            QueueOutgoingTransfer(new QueuedOutgoingTransfer(id, user, request));
            return;
        }

        TransferRoutine(user, request.FileNameString, request.Clientbound, request.Hotload, id).Run();
    }

    static void QueueOutgoingTransfer(QueuedOutgoingTransfer transfer)
    {
        bool started;
        int activeCount;
        int queuedCount;

        lock (outgoingTransferLock)
        {
            started = outgoingTransferScheduler.EnqueueOrStart(transfer, StartOutgoingTransfer);
            activeCount = outgoingTransferScheduler.ActiveCount;
            queuedCount = outgoingTransferScheduler.QueuedCount;
        }

        if (!started)
        {
            VWorld.Log.LogInfo(
                $"[VShare] Transfer queued: {transfer.Request.FileNameString} (transfer {transfer.TransferId}, " +
                $"active {activeCount}, queued {queuedCount}, max {MaxActiveOutgoingTransfers}).");
        }
    }

    static void StartOutgoingTransfer(QueuedOutgoingTransfer transfer)
    {
        VWorld.Log.LogDebug(
            $"[VShare] Transfer dispatch starting: {transfer.Request.FileNameString} (transfer {transfer.TransferId}).");
        OutgoingTransferRoutine(transfer).Run();
    }

    static IEnumerator OutgoingTransferRoutine(QueuedOutgoingTransfer transfer)
    {
        try
        {
            IEnumerator routine = TransferRoutine(
                transfer.Target,
                transfer.Request.FileNameString,
                transfer.Request.Clientbound,
                transfer.Request.Hotload,
                transfer.TransferId);

            while (routine.MoveNext())
            {
                yield return routine.Current;
            }
        }
        finally
        {
            CompleteOutgoingTransfer(transfer.TransferId);
        }
    }

    static void CompleteOutgoingTransfer(Guid transferId)
    {
        int activeCount;
        int queuedCount;

        lock (outgoingTransferLock)
        {
            outgoingTransferScheduler.Complete(transferId, StartOutgoingTransfer);
            activeCount = outgoingTransferScheduler.ActiveCount;
            queuedCount = outgoingTransferScheduler.QueuedCount;
        }

        VWorld.Log.LogDebug(
            $"[VShare] Transfer dispatch completed: {transferId} (active {activeCount}, queued {queuedCount}).");
    }

    static int GetActiveOutgoingTransferCount()
    {
        lock (outgoingTransferLock)
        {
            return outgoingTransferScheduler.ActiveCount;
        }
    }

    static int GetQueuedOutgoingTransferCount()
    {
        lock (outgoingTransferLock)
        {
            return outgoingTransferScheduler.QueuedCount;
        }
    }

    static IReadOnlyList<QueuedOutgoingTransfer> RemoveQueuedOutgoingTransfersForUser(ulong targetId)
    {
        lock (outgoingTransferLock)
        {
            return outgoingTransferScheduler.RemoveQueued(transfer => transfer.TargetId == targetId);
        }
    }

    /// <summary>
    /// Sends a user-initiated request to the server to offer any shared mods missing on the client.
    /// </summary>
    public static void RequestSharedModsFromMenu()
    {
        if (!VWorld.IsClient)
        {
            return;
        }

        ClientModSignature[] signatures = BuildClientModSignatures();
        ArmSharedModAutoAcceptWindow(_latestSharedClientModPreview);
        RecordSharedModRequestConsent(GetUtcNow());
        API.Shared.VNetwork.SendToServer(new RequestSharedClientMods(signatures));
        VWorld.Log?.LogInfo($"Requested server-shared mods with {signatures.Length} local plugin signature(s).");
    }

    /// <summary>
    /// Gets the current VShare menu detail text for the request button.
    /// </summary>
    /// <returns>The hover/details text to show in the options menu.</returns>
    public static string GetSharedModsMenuDescription()
        => BuildSharedModsMenuDescription(_latestSharedClientModPreview);

    /// <summary>
    /// Sends the current VShare catalog after a client completes the authenticated session.
    /// </summary>
    /// <param name="user">The newly ready client user.</param>
    static void OnVNetworkReady(User user)
    {
        if (!VWorld.IsServer)
        {
            return;
        }

        SendSharedClientModCatalog(user);
    }

    /// <summary>
    /// Handles the client request by sending transfer offers for missing shared mods.
    /// </summary>
    /// <param name="user">The requesting client.</param>
    /// <param name="request">The request payload describing client mods.</param>
    static void HandleSharedModsRequest(User user, RequestSharedClientMods request)
    {
        if (!VWorld.IsServer)
        {
            return;
        }

        IReadOnlyList<SharedModEntry> sharedEntries = GetServerShareEntries();
        if (sharedEntries.Count == 0)
        {
            return;
        }

        ClientModLookup clientMods = BuildClientModLookup(request.ClientMods);
        VWorld.Log.LogInfo(
            $"Received shared mod request from {user.PlatformId}; " +
            $"{request.ClientMods?.Length ?? 0} client signature(s), {sharedEntries.Count} staged share(s).");
        var eligibleOffers = new List<SharedModOfferCandidate>();

        foreach (SharedModEntry entry in sharedEntries)
        {
            if (!IsShareRequired(entry, clientMods))
            {
                continue;
            }

            if (!TryGetClientShareMetadata(
                    entry,
                    out PluginShareMetadataStore.PluginShareMetadata metadata,
                    out string skipReason))
            {
                VWorld.Log.LogWarning($"Skipping shared mod {entry.FileName}: {skipReason}");
                continue;
            }

            if (!IsFileNameWithinLimit(entry.FileName))
            {
                VWorld.Log.LogWarning($"Skipping shared mod {entry.FileName}: file name exceeds packet length.");
                continue;
            }

            bool hotload = IsSharedModHotloadAllowed(entry.IsZip, metadata);
            eligibleOffers.Add(new SharedModOfferCandidate(entry, metadata, hotload));
        }

        foreach (SharedModOfferCandidate offer in eligibleOffers)
        {
            SendTransferOffer(user, new TransferRequest(offer.Entry.FileName.AsSpan(), clientbound: true, offer.Hotload));
        }
    }
    static IEnumerator TransferRoutine(User target, string fileName, bool clientbound, bool hotload = false, Guid? transferId = null)
    {
        bool isZip = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string pluginName = Path.GetFileNameWithoutExtension(fileName);
        byte[] fileBytes = null;
        byte[] rawBytes = null;
        ReleaseDigestVerificationResult verificationResult = default;

        if (clientbound)
        {
            if (!(isZip
                ? VShare.TryGetCachedRawZip(pluginName, true, out rawBytes)
                : VShare.TryGetCachedRawDll(fileName, true, out rawBytes)))
            {
                string message = $"Unable to read raw plugin bytes for {fileName}. Transfer aborted.";
                VWorld.Log.LogWarning(message);
                SendTransferFailureMessage(target, message);
                yield break;
            }

            yield return VerifyShareDigestRoutine(target, fileName, pluginName, rawBytes, result => verificationResult = result);
            if (verificationResult.ShouldAbort)
            {
                yield break;
            }
        }

        if (isZip)
        {
            if (!VShare.TryGetCachedZip(pluginName, clientbound, out fileBytes))
            {
                VWorld.Log.LogWarning($"Unable to locate {fileName} in cache directory...");
                yield break;
            }

            if (!clientbound)
            {
                bool decompressionDone = false;
                using var decompressedStream = new MemoryStream();
                yield return DecompressChunkRoutine(
                    fileBytes,
                    slice => decompressedStream.Write(slice, 0, slice.Length),
                    () => decompressionDone = true,
                    Guid.Empty);
                while (!decompressionDone)
                {
                    yield return null;
                }

                using var mem = new MemoryStream(decompressedStream.ToArray());
                using var archive = new ZipArchive(mem, ZipArchiveMode.Read);
                if (!TryGetZipEntryDestinations(archive, Paths.GameRootPath, fileName, out var destinations))
                {
                    VWorld.Log.LogWarning($"Aborting transfer for {fileName} due to invalid zip entry.");
                    yield break;
                }

                var extractRoutine = ExtractZipArchiveRoutine(destinations);
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = extractRoutine.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        VWorld.Log.LogError($"Package extraction failed: {ex}");
                        yield break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return extractRoutine.Current;
                }

                VWorld.Log.LogInfo($"[VShare] Extracted {fileName} to {Paths.GameRootPath}.");

                yield break;
            }
        }
        else
        {
            if (VShare.TryGetCachedDll(fileName, clientbound, out byte[] cachedBytes))
            {
                string operation = clientbound ? "Sending" : "Writing";
                VWorld.Log.LogInfo($"[VShare] {operation} {fileName} from cache.");

                if (!clientbound)
                {
                    bool decompressionDone = false;
                    using var decompressedStream = new MemoryStream();
                    yield return DecompressChunkRoutine(
                        cachedBytes,
                        slice => decompressedStream.Write(slice, 0, slice.Length),
                        () => decompressionDone = true,
                        Guid.Empty);

                    while (!decompressionDone)
                    {
                        yield return null;
                    }

                    fileBytes = decompressedStream.ToArray();

                    string filePath = Path.Combine(Paths.PluginPath, fileName);
                    TransferFileWriteResult WriteResult = default;
                    bool WriteComplete = false;
                    EnqueueTransferWorkItem(new TransferFileWriteWorkItem(
                        Guid.Empty,
                        filePath,
                        fileBytes,
                        result =>
                        {
                            WriteResult = result;
                            WriteComplete = true;
                        }));

                    while (!WriteComplete)
                    {
                        yield return null;
                    }

                    if (!WriteResult.Success)
                    {
                        VWorld.Log.LogError($"File write / load error: {WriteResult.ErrorMessage}");
                        yield break;
                    }
                    else if (!File.Exists(filePath))
                    {
                        VWorld.Log.LogWarning("Failed writing to disk...");
                    }
                    else
                    {
                        VWorld.Log.LogInfo(FormatTransferCompleteMessage(fileName, fileBytes.Length));

                        if (hotload)
                        {
                            VWorld.Log.LogInfo($"[VShare] Loading plugin: {fileName}.");
                            LogHotloadPluginResult(filePath, LoadPlugin(filePath));
                        }
                    }

                    yield break;
                }
                else
                {
                    fileBytes = cachedBytes;
                }
            }
            else
            {
                VWorld.Log.LogWarning($"Unable to locate {fileName} in cache directory...");
                yield break;
            }
        }

        Guid id = transferId ?? Guid.NewGuid();
        byte[] Hash = null;
        bool HashComplete = false;
        QueueTransferHashWork(
            id,
            fileBytes,
            result =>
            {
                Hash = result;
                HashComplete = true;
            });

        while (!HashComplete)
        {
            yield return null;
        }

        byte[] releaseDigestBytes = verificationResult.ReleaseDigestBytes ?? [];
        ushort chunkSize = Const.PACKET_BYTES;

        VWorld.Log.LogInfo(FormatTransferDispatchMessage(fileName, fileBytes.Length, id));

        TransferSession init = new(
            id,
            fileBytes.Length,
            fileName.AsSpan(),
            Hash.AsSpan(),
            releaseDigestBytes.AsSpan());
        API.Shared.VNetwork.SendToClient(target, init);

        bool SendComplete = false;
        EnqueueTransferWorkItem(new TransferChunkSendWorkItem(
            id,
            target,
            fileBytes,
            chunkSize,
            hotload,
            () => SendComplete = true));

        while (!SendComplete)
        {
            yield return null;
        }
    }
    static void OnTransferSession(User sender, TransferSession session)
    {
        VWorld.Log.LogInfo(FormatTransferStartMessage(session.FileNameString, session.TotalBytes));
        RegisterIncomingTransfer(session, sender.PlatformId);
    }
    /// <summary>
    /// Registers an incoming transfer in the tracking store.
    /// </summary>
    /// <param name="session">The transfer session to track.</param>
    /// <param name="sourcePlatformId">The platform identifier for the sender.</param>
    /// <returns>The tracked incoming transfer.</returns>
    static IncomingTransfer RegisterIncomingTransfer(TransferSession session, ulong sourcePlatformId)
    {
        IncomingTransfer incoming = new(session, sourcePlatformId);

        lock (incomingLock)
        {
            if (incomingTransfers.TryGetValue(session.Id, out var existingTransfer) && existingTransfer.IsComplete == false)
            {
                VWorld.Log.LogWarning($"Replacing active transfer session {session.Id} for {existingTransfer.FileName}.");
            }

            incomingTransfers[session.Id] = incoming;
        }

        return incoming;
    }
    /// <summary>
    /// Stores a newly received transfer offer on the client.
    /// </summary>
    /// <param name="offer">The transfer offer received from the server.</param>
    static void OnTransferOffer(TransferOffer offer)
    {
        if (!VWorld.IsClient)
        {
            return;
        }

        DateTime expiresAt = GetUtcNow().Add(offerTimeout);

        lock (pendingOfferLock)
        {
            pendingClientOffers[offer.Id] = new PendingClientOffer(offer, expiresAt);
        }

        VWorld.Log.LogInfo(
            $"[VShare] Offer received: {offer.FileNameString} (offer {offer.Id}, expires {expiresAt:HH\\:mm\\:ss}).");

        if (ShouldAutoAcceptSharedModOffer(offer, GetUtcNow()))
        {
            VWorld.Log.LogInfo(
                $"[VShare] Auto-accepting catalog-listed requested share: {offer.FileNameString} (offer {offer.Id}).");
            TryAcceptTransferOffer(offer.Id);
        }
    }

    /// <summary>
    /// Updates the client-side VShare menu preview after a server response.
    /// </summary>
    /// <param name="preview">The latest server preview payload.</param>
    static void OnSharedClientModPreview(SharedClientModPreview preview)
    {
        if (!VWorld.IsClient)
        {
            return;
        }

        _latestSharedClientModPreview = preview.Entries ?? [];
        VWorld.Log.LogInfo(
            $"[VShare] Received server shared-mod catalog with {_latestSharedClientModPreview.Length} entries.");
        if (VWorld.IsClient)
        {
            OptionsManager.RefreshButtonDescription(REQUEST_SHARED_MODS_BUTTON_ID);
        }
    }
    /// <summary>
    /// Handles a client acceptance by starting the transfer on the server.
    /// </summary>
    /// <param name="user">The user who accepted the offer.</param>
    /// <param name="accept">The acceptance payload.</param>
    static void OnTransferAccept(User user, TransferAccept accept)
    {
        if (!VWorld.IsServer)
        {
            return;
        }

        PendingServerOffer pendingOffer = null;

        lock (pendingOfferLock)
        {
            if (!pendingServerOffers.TryGetValue(accept.Id, out pendingOffer))
            {
                VWorld.Log.LogWarning($"Transfer accept ignored ~ ID: {accept.Id} (no pending offer).");
                return;
            }

            if (pendingOffer.TargetId != user.PlatformId)
            {
                VWorld.Log.LogWarning(
                    $"Transfer accept rejected ~ ID: {accept.Id} (target mismatch).");
                return;
            }

            if (pendingOffer.ExpiresAt <= GetUtcNow())
            {
                pendingServerOffers.Remove(accept.Id);
                VWorld.Log.LogWarning(
                    $"Transfer accept expired ~ ID: {accept.Id} | Plugin: {pendingOffer.Offer.FileNameString}.");
                return;
            }

            pendingServerOffers.Remove(accept.Id);
        }

        VWorld.Log.LogInfo(
            $"[VShare] Offer accepted: {pendingOffer.Offer.FileNameString} by {user.PlatformId} (offer {accept.Id}).");
        InternalTransferRequest(user, pendingOffer.Request, pendingOffer.Offer.Id);
    }
    /// <summary>
    /// Handles a client decline by clearing the pending offer on the server.
    /// </summary>
    /// <param name="user">The user who declined the offer.</param>
    /// <param name="decline">The decline payload.</param>
    static void OnTransferDecline(User user, TransferDecline decline)
    {
        if (!VWorld.IsServer)
        {
            return;
        }

        PendingServerOffer pendingOffer = null;

        lock (pendingOfferLock)
        {
            if (!pendingServerOffers.TryGetValue(decline.Id, out pendingOffer))
            {
                VWorld.Log.LogWarning($"Transfer decline ignored ~ ID: {decline.Id} (no pending offer).");
                return;
            }

            if (pendingOffer.TargetId != user.PlatformId)
            {
                VWorld.Log.LogWarning(
                    $"Transfer decline rejected ~ ID: {decline.Id} (target mismatch).");
                return;
            }

            pendingServerOffers.Remove(decline.Id);
        }

        VWorld.Log.LogWarning(
            $"Transfer declined ~ ID: {decline.Id} | Plugin: {pendingOffer.Offer.FileNameString} | Target: {user.PlatformId} | Reason: {decline.Reason}");
    }
    static void OnTransferChunk(TransferChunk chunk)
    {
        lock (incomingLock)
        {
            if (!incomingTransfers.ContainsKey(chunk.Id))
            {
                VWorld.Log.LogWarning($"Received chunk for unknown transfer {chunk.Id}.");
                return;
            }
        }

        EnqueueTransferWorkItem(new TransferChunkReceiveWorkItem(
            chunk.Id,
            chunk.Index,
            chunk.ChunkBytes));
    }
    static void OnTransferComplete(TransferComplete complete)
    {
        EnqueueTransferWorkItem(new TransferCompleteWorkItem(complete));
    }

    static void ProcessTransferComplete(TransferComplete complete)
    {
        lock (incomingLock)
        {
            if (!incomingTransfers.TryGetValue(complete.Id, out var incoming))
            {
                VWorld.Log.LogWarning($"Received completion for unknown transfer {complete.Id}.");
                return;
            }

            incoming.MarkCompletionReceived(complete);
        }

        TryFinalizeIncomingTransfer(complete.Id);
    }

    /// <summary>
    /// Clears pending transfer offers when a user disconnects.
    /// </summary>
    /// <param name="userDisconnected">The disconnect event data.</param>
    static void OnUserDisconnected(UserDisconnected userDisconnected)
    {
        if (!VWorld.IsServer)
        {
            return;
        }

        List<PendingServerOffer> removedOffers = [];
        ulong targetId = userDisconnected.PlayerInfo.SteamId;

        lock (pendingOfferLock)
        {
            foreach (var entry in pendingServerOffers)
            {
                if (entry.Value.TargetId == targetId)
                {
                    removedOffers.Add(entry.Value);
                }
            }

            foreach (var offer in removedOffers)
            {
                pendingServerOffers.Remove(offer.Offer.Id);
            }
        }

        foreach (var offer in removedOffers)
        {
            VWorld.Log.LogWarning(
                $"Transfer offer cleared on disconnect ~ ID: {offer.Offer.Id} | Plugin: {offer.Offer.FileNameString} | Target: {targetId}");
        }

        List<IncomingTransfer> removedTransfers = RemoveIncomingTransfersForUser(targetId);
        foreach (IncomingTransfer transfer in removedTransfers)
        {
            VWorld.Log.LogWarning(
                $"Incoming transfer cleared on disconnect ~ ID: {transfer.Id} | Plugin: {transfer.FileName} | Target: {targetId}");
        }

        IReadOnlyList<QueuedOutgoingTransfer> removedOutgoingTransfers = RemoveQueuedOutgoingTransfersForUser(targetId);
        foreach (QueuedOutgoingTransfer transfer in removedOutgoingTransfers)
        {
            VWorld.Log.LogWarning(
                $"Queued outgoing transfer cleared on disconnect ~ ID: {transfer.TransferId} | Plugin: {transfer.Request.FileNameString} | Target: {targetId}");
        }
    }
    static IEnumerator IncomingRoutine(TransferComplete complete, IncomingTransfer incoming)
    {
        bool isZip = incoming.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string filePath = isZip ? string.Empty : Path.Combine(Paths.PluginPath, incoming.FileName);
        string tempFilePath = BuildIncomingTempFilePath(incoming.FileName, incoming.Id);
        bool allowOverwrite = TryConsumeOverwriteConfirmation(incoming.Id, incoming.FileName);

        if (!isZip && !TryHandleExistingDll(filePath, incoming, allowOverwrite))
        {
            yield break;
        }

        byte[] computedHash = Array.Empty<byte>();
        FileStream outputStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        SHA256 sha = SHA256.Create();
        var decompressionRoutine = DecompressChunkRoutine(
            incoming.Concat(),
            slice =>
            {
                outputStream.Write(slice, 0, slice.Length);
                sha.TransformBlock(slice, 0, slice.Length, null, 0);
            },
            () => { },
            incoming.Id);

        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = decompressionRoutine.MoveNext();
            }
            catch (Exception ex)
            {
                VWorld.Log.LogError($"Failed to write decompressed payload for {incoming.FileName}: {ex}");
                outputStream.Dispose();
                sha.Dispose();
                DeleteTemporaryFileIfPresent(tempFilePath);
                yield break;
            }

            if (!hasNext)
            {
                break;
            }

            yield return decompressionRoutine.Current;
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        computedHash = sha.Hash ?? Array.Empty<byte>();
        outputStream.Dispose();
        sha.Dispose();

        if (!incoming.TryValidateReleaseAssetDigest(computedHash, isZip, out string validationError))
        {
            VWorld.Log.LogWarning(validationError);
            DeleteTemporaryFileIfPresent(tempFilePath);
            yield break;
        }

        if (isZip)
        {
            using var archiveStream = File.OpenRead(tempFilePath);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            if (!TryGetZipEntryDestinations(archive, Paths.GameRootPath, incoming.FileName, out var destinations))
            {
                VWorld.Log.LogWarning($"Aborting transfer for {incoming.FileName} due to invalid zip entry.");
                DeleteTemporaryFileIfPresent(tempFilePath);
                yield break;
            }

            if (!TryHandleZipCollisions(destinations, incoming.FileName, allowOverwrite))
            {
                DeleteTemporaryFileIfPresent(tempFilePath);
                yield break;
            }

            var extractRoutine = ExtractZipArchiveRoutine(destinations);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = extractRoutine.MoveNext();
                }
                catch (Exception ex)
                {
                    VWorld.Log.LogError($"Zip extraction failed: {ex}");
                    DeleteTemporaryFileIfPresent(tempFilePath);
                    yield break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return extractRoutine.Current;
            }

            VWorld.Log.LogInfo($"[VShare] Extracted {incoming.FileName} to {Paths.GameRootPath}.");
            DeleteTemporaryFileIfPresent(tempFilePath);

            yield break;
        }

        try
        {
            FinalizeIncomingFile(tempFilePath, filePath, allowOverwrite);
            VWorld.Log.LogInfo(FormatTransferCompleteMessage(incoming.FileName, incoming.TotalBytes));

            if (complete.Hotload)
            {
                VWorld.Log.LogInfo($"[VShare] Loading plugin: {incoming.FileName}.");
                LogHotloadPluginResult(filePath, LoadPlugin(filePath));
            }
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"File write / load error: {ex}");
            DeleteTemporaryFileIfPresent(tempFilePath);
        }

        // VNetwork.SendToServer(new FileAck(done.Id, success));
    }

    /// <summary>
    /// Starts the background coroutine that prunes expired transfer offers.
    /// </summary>
    static void StartOfferCleanup()
    {
        if (offerCleanupStarted)
        {
            return;
        }

        offerCleanupStarted = true;
        OfferCleanupRoutine().Run();
    }

    /// <summary>
    /// Periodically removes expired offers and notifies the server of timeouts.
    /// </summary>
    /// <returns>An enumerator for coroutine execution.</returns>
    static IEnumerator OfferCleanupRoutine()
    {
        while (true)
        {
            CleanupExpiredOffers();
            yield return offerCleanupDelay;
        }
    }

    /// <summary>
    /// Starts the background coroutine that prunes stale incoming transfers.
    /// </summary>
    static void StartIncomingTransferCleanup()
    {
        if (incomingCleanupStarted)
        {
            return;
        }

        incomingCleanupStarted = true;
        IncomingTransferCleanupRoutine().Run();
    }

    /// <summary>
    /// Periodically removes incoming transfers that have exceeded the idle timeout.
    /// </summary>
    /// <returns>An enumerator for coroutine execution.</returns>
    static IEnumerator IncomingTransferCleanupRoutine()
    {
        while (true)
        {
            CleanupStaleIncomingTransfers();
            yield return incomingCleanupDelay;
        }
    }

    /// <summary>
    /// Starts the background coroutine that advances the transfer work queue.
    /// </summary>
    static void StartTransferWorkQueue()
    {
        if (transferWorkQueueStarted)
        {
            return;
        }

        transferWorkQueueStarted = true;
        TransferWorkQueueRoutine().Run();
    }

    /// <summary>
    /// Processes transfer work items within a fixed time budget each frame.
    /// </summary>
    /// <returns>An enumerator for coroutine execution.</returns>
    static IEnumerator TransferWorkQueueRoutine()
    {
        while (true)
        {
            ProcessTransferWorkQueue(TimeSpan.FromMilliseconds(TransferWorkQueueBudgetMs));
            yield return null;
        }
    }

    /// <summary>
    /// Processes transfer work items up to the specified time budget.
    /// </summary>
    /// <param name="timeBudget">The time budget to apply.</param>
    /// <returns>The progress for the processing pass.</returns>
    static TransferWorkQueueProgress ProcessTransferWorkQueue(TimeSpan timeBudget)
    {
        if (transferWorkQueue.Count == 0)
        {
            return new TransferWorkQueueProgress(0, 0, false);
        }

        TransferWorkQueueProgress Progress = transferWorkQueue.Process(timeBudget, MaxTransferWorkStepsPerFrame);
        LogTransferWorkQueueProgress(Progress);
        return Progress;
    }

    /// <summary>
    /// Enqueues a transfer work item for scheduled processing.
    /// </summary>
    /// <param name="workItem">The work item to enqueue.</param>
    static void EnqueueTransferWorkItem(ITransferWorkItem workItem)
    {
        transferWorkQueue.Enqueue(workItem);
    }

    /// <summary>
    /// Logs transfer work queue progress on a limited cadence.
    /// </summary>
    /// <param name="progress">The progress snapshot.</param>
    static void LogTransferWorkQueueProgress(TransferWorkQueueProgress progress)
    {
        if (!ShouldLogTransferWorkQueueProgress(progress))
        {
            return;
        }

        VWorld.Log?.LogDebug(
            $"Transfer work queue processed {progress.StepsProcessed} step(s), " +
            $"{progress.TransfersCompleted} transfer(s) completed, " +
            $"budget exceeded: {progress.BudgetExceeded}, " +
            $"step cap exceeded: {progress.StepLimitExceeded}, " +
            $"active outgoing: {GetActiveOutgoingTransferCount()}, " +
            $"queued outgoing: {GetQueuedOutgoingTransferCount()}.");
    }

    /// <summary>
    /// Determines whether transfer work queue progress should be logged.
    /// </summary>
    /// <param name="progress">The progress snapshot.</param>
    /// <returns><c>true</c> when logging should occur; otherwise, <c>false</c>.</returns>
    static bool ShouldLogTransferWorkQueueProgress(TransferWorkQueueProgress progress)
    {
        if (!progress.BudgetExceeded && !progress.StepLimitExceeded)
        {
            return false;
        }

        DateTime Now = GetUtcNow();
        if (Now - lastTransferWorkQueueLogUtc < TimeSpan.FromSeconds(TRANSFER_WORK_QUEUE_LOG_INTERVAL_SECONDS))
        {
            return false;
        }

        lastTransferWorkQueueLogUtc = Now;
        return true;
    }

    /// <summary>
    /// Formats the client-side start line for an incoming VShare transfer.
    /// </summary>
    /// <param name="fileName">The staged file name.</param>
    /// <param name="totalBytes">The total transfer size.</param>
    /// <returns>A concise operator-facing log line.</returns>
    static string FormatTransferStartMessage(string fileName, int totalBytes)
        => $"[VShare] Transfer started: {fileName} ({totalBytes.PrettyBytes()}).";

    /// <summary>
    /// Formats the server-side dispatch line for an outgoing VShare transfer.
    /// </summary>
    /// <param name="fileName">The staged file name.</param>
    /// <param name="totalBytes">The total transfer size.</param>
    /// <param name="transferId">The transfer identifier.</param>
    /// <returns>A concise operator-facing log line.</returns>
    static string FormatTransferDispatchMessage(string fileName, int totalBytes, Guid transferId)
        => $"[VShare] Sending {fileName} ({totalBytes.PrettyBytes()}, transfer {transferId}).";

    /// <summary>
    /// Formats a coarse incoming VShare download progress line.
    /// </summary>
    /// <param name="fileName">The staged file name.</param>
    /// <param name="receivedBytes">The received byte count.</param>
    /// <param name="totalBytes">The total transfer size.</param>
    /// <returns>A concise operator-facing progress line.</returns>
    static string FormatTransferProgressMessage(string fileName, int receivedBytes, int totalBytes)
    {
        int percent = CalculateTransferPercent(receivedBytes, totalBytes);
        int filled = Math.Clamp(percent * TRANSFER_PROGRESS_BAR_WIDTH / 100, 0, TRANSFER_PROGRESS_BAR_WIDTH);
        string progressBar =
            new string('#', filled) +
            new string('-', TRANSFER_PROGRESS_BAR_WIDTH - filled);

        return
            $"[VShare] Downloading {fileName} [{progressBar}] {percent}% " +
            $"({receivedBytes.PrettyBytes()} / {totalBytes.PrettyBytes()}).";
    }

    /// <summary>
    /// Determines whether an incoming transfer crossed a new progress milestone.
    /// </summary>
    /// <param name="receivedBytes">The received byte count.</param>
    /// <param name="totalBytes">The total transfer size.</param>
    /// <param name="lastLoggedPercent">The last logged milestone.</param>
    /// <param name="nextLoggedPercent">The next milestone to remember.</param>
    /// <returns><c>true</c> when progress should be logged.</returns>
    static bool ShouldLogTransferProgress(
        int receivedBytes,
        int totalBytes,
        int lastLoggedPercent,
        out int nextLoggedPercent)
    {
        int percent = CalculateTransferPercent(receivedBytes, totalBytes);
        int milestone = Math.Min(
            75,
            percent / TRANSFER_PROGRESS_LOG_PERCENT_STEP * TRANSFER_PROGRESS_LOG_PERCENT_STEP);

        if (milestone <= 0 || milestone <= lastLoggedPercent)
        {
            nextLoggedPercent = lastLoggedPercent;
            return false;
        }

        nextLoggedPercent = milestone;
        return true;
    }

    /// <summary>
    /// Formats the client-side completion line for a VShare download.
    /// </summary>
    /// <param name="fileName">The staged file name.</param>
    /// <param name="totalBytes">The total transfer size.</param>
    /// <returns>A concise operator-facing completion line.</returns>
    static string FormatTransferCompleteMessage(string fileName, int totalBytes)
        => $"[VShare] Download complete: {fileName} ({totalBytes.PrettyBytes()}, digest verified).";

    /// <summary>
    /// Calculates a clamped integer transfer percentage.
    /// </summary>
    /// <param name="receivedBytes">The received byte count.</param>
    /// <param name="totalBytes">The total transfer size.</param>
    /// <returns>A value from 0 through 100.</returns>
    static int CalculateTransferPercent(int receivedBytes, int totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 0;
        }

        double percent = Math.Floor((double)Math.Max(0, receivedBytes) * 100 / totalBytes);
        return Math.Clamp((int)percent, 0, 100);
    }

    /// <summary>
    /// Removes expired offers for the current runtime context.
    /// </summary>
    static void CleanupExpiredOffers()
    {
        DateTime now = GetUtcNow();
        List<TransferOffer> expiredClientOffers = null;
        List<PendingServerOffer> expiredServerOffers = null;

        lock (pendingOfferLock)
        {
            if (VWorld.IsClient)
            {
                foreach (var entry in pendingClientOffers)
                {
                    if (entry.Value.ExpiresAt <= now)
                    {
                        expiredClientOffers ??= [];
                        expiredClientOffers.Add(entry.Value.Offer);
                    }
                }

                if (expiredClientOffers != null)
                {
                    foreach (var offer in expiredClientOffers)
                    {
                        pendingClientOffers.Remove(offer.Id);
                    }
                }
            }

            if (VWorld.IsServer)
            {
                foreach (var entry in pendingServerOffers)
                {
                    if (entry.Value.ExpiresAt <= now)
                    {
                        expiredServerOffers ??= [];
                        expiredServerOffers.Add(entry.Value);
                    }
                }

                if (expiredServerOffers != null)
                {
                    foreach (var offer in expiredServerOffers)
                    {
                        pendingServerOffers.Remove(offer.Offer.Id);
                    }
                }
            }
        }

        if (VWorld.IsClient)
        {
            CleanupExpiredOverwriteConfirmations(now);
        }

        if (expiredClientOffers != null)
        {
            foreach (var offer in expiredClientOffers)
            {
                VWorld.Log.LogWarning(
                    $"Transfer offer expired ~ ID: {offer.Id} | Plugin: {offer.FileNameString}");
                TrySendDeclineForExpiredOffer(offer.Id);
            }
        }

        if (expiredServerOffers != null)
        {
            foreach (var offer in expiredServerOffers)
            {
                VWorld.Log.LogWarning(
                    $"Transfer offer timed out ~ ID: {offer.Offer.Id} | Plugin: {offer.Offer.FileNameString} | Target: {offer.TargetId}");
            }
        }
    }

    /// <summary>
    /// Logs an incoming transfer timeout when logging is available.
    /// </summary>
    /// <param name="transfer">The timed-out transfer.</param>
    /// <param name="idleDuration">The duration the transfer was idle.</param>
    static void LogIncomingTransferTimeout(IncomingTransfer transfer, TimeSpan idleDuration)
    {
        if (VWorld.Log == null)
        {
            return;
        }

        VWorld.Log.LogWarning(
            $"Incoming transfer timed out ~ ID: {transfer.Id} | Plugin: {transfer.FileName} | Idle: {idleDuration.TotalSeconds:0.##}s");
    }

    /// <summary>
    /// Removes incoming transfers that have not received chunks within the configured timeout.
    /// </summary>
    static void CleanupStaleIncomingTransfers()
    {
        if (VWorld.IsClient && !API.Shared.VNetwork.IsReady)
        {
            ClearIncomingTransfers("client network is not ready");
            return;
        }

        DateTime now = GetUtcNow();
        List<IncomingTransfer> expiredTransfers = null;

        lock (incomingLock)
        {
            foreach (var entry in incomingTransfers)
            {
                TimeSpan idleDuration = now - entry.Value.LastChunkUtc;
                if (idleDuration >= incomingTransferTimeout)
                {
                    expiredTransfers ??= [];
                    expiredTransfers.Add(entry.Value);
                }
            }

            if (expiredTransfers != null)
            {
                foreach (IncomingTransfer transfer in expiredTransfers)
                {
                    incomingTransfers.Remove(transfer.Id);
                }
            }
        }

        if (expiredTransfers == null)
        {
            return;
        }

        foreach (IncomingTransfer transfer in expiredTransfers)
        {
            TimeSpan idleDuration = now - transfer.LastChunkUtc;
            IncomingTransferTimedOut?.Invoke(transfer, idleDuration);
            LogIncomingTransferTimeout(transfer, idleDuration);
        }
    }

    /// <summary>
    /// Registers an incoming transfer for testing.
    /// </summary>
    /// <param name="session">The transfer session to register.</param>
    /// <param name="sourcePlatformId">The source platform identifier.</param>
    /// <returns>The tracked incoming transfer.</returns>
    internal static IncomingTransfer RegisterIncomingTransferForTesting(TransferSession session, ulong sourcePlatformId)
    {
        return RegisterIncomingTransfer(session, sourcePlatformId);
    }

    /// <summary>
    /// Executes incoming transfer cleanup for testing.
    /// </summary>
    internal static void CleanupStaleIncomingTransfersForTesting()
    {
        CleanupStaleIncomingTransfers();
    }

    /// <summary>
    /// Determines whether an incoming transfer is currently tracked.
    /// </summary>
    /// <param name="transferId">The transfer identifier to check.</param>
    /// <returns><c>true</c> if the transfer is tracked; otherwise, <c>false</c>.</returns>
    internal static bool HasIncomingTransferForTesting(Guid transferId)
    {
        lock (incomingLock)
        {
            return incomingTransfers.ContainsKey(transferId);
        }
    }

    /// <summary>
    /// Gets the number of bytes received for a transfer under test.
    /// </summary>
    /// <param name="transferId">The transfer identifier.</param>
    /// <returns>The received byte count, or <c>-1</c> when the transfer is not tracked.</returns>
    internal static int GetIncomingReceivedBytesForTesting(Guid transferId)
    {
        lock (incomingLock)
        {
            return incomingTransfers.TryGetValue(transferId, out IncomingTransfer incoming)
                ? incoming.ReceivedBytes
                : -1;
        }
    }

    /// <summary>
    /// Resets incoming transfer state for testing.
    /// </summary>
    internal static void ResetIncomingTransferStateForTesting()
    {
        lock (incomingLock)
        {
            incomingTransfers.Clear();
        }

        IncomingTransferTimedOut = null;
    }

    /// <summary>
    /// Gets the number of queued transfer work items for testing.
    /// </summary>
    /// <returns>The current queue count.</returns>
    internal static int GetTransferWorkQueueCountForTesting()
    {
        return transferWorkQueue.Count;
    }

    /// <summary>
    /// Clears the transfer work queue for testing.
    /// </summary>
    internal static void ResetTransferWorkQueueForTesting()
    {
        transferWorkQueue.Clear();
    }

    /// <summary>
    /// Queues transfer setup hashing work for testing.
    /// </summary>
    /// <param name="payload">The payload to hash.</param>
    internal static void QueueTransferSetupHashForTesting(byte[] payload)
    {
        QueueTransferHashWork(Guid.NewGuid(), payload, _ => { });
    }

    /// <summary>
    /// Enqueues a transfer work item for testing.
    /// </summary>
    /// <param name="workItem">The work item to enqueue.</param>
    internal static void EnqueueTransferWorkItemForTesting(ITransferWorkItem workItem)
    {
        EnqueueTransferWorkItem(workItem);
    }

    /// <summary>
    /// Queues an incoming transfer chunk for testing.
    /// </summary>
    /// <param name="transferId">The transfer identifier.</param>
    /// <param name="index">The chunk index.</param>
    /// <param name="bytes">The chunk payload.</param>
    internal static void QueueTransferChunkForTesting(Guid transferId, int index, byte[] bytes)
    {
        EnqueueTransferWorkItem(new TransferChunkReceiveWorkItem(transferId, index, bytes));
    }

    /// <summary>
    /// Queues transfer completion for testing.
    /// </summary>
    /// <param name="complete">The completion packet.</param>
    internal static void QueueTransferCompleteForTesting(TransferComplete complete)
    {
        OnTransferComplete(complete);
    }

    /// <summary>
    /// Processes transfer work for testing using the specified budget.
    /// </summary>
    /// <param name="timeBudget">The processing time budget.</param>
    /// <returns>The progress summary for the pass.</returns>
    internal static TransferWorkQueueProgress ProcessTransferWorkQueueForTesting(TimeSpan timeBudget)
    {
        return ProcessTransferWorkQueue(timeBudget);
    }

    /// <summary>
    /// Removes incoming transfers that were associated with the specified user identifier.
    /// </summary>
    /// <param name="platformId">The platform identifier to match.</param>
    /// <returns>The transfers that were removed.</returns>
    static List<IncomingTransfer> RemoveIncomingTransfersForUser(ulong platformId)
    {
        List<IncomingTransfer> removedTransfers = null;

        lock (incomingLock)
        {
            foreach (var entry in incomingTransfers)
            {
                if (entry.Value.SourcePlatformId == platformId)
                {
                    removedTransfers ??= [];
                    removedTransfers.Add(entry.Value);
                }
            }

            if (removedTransfers != null)
            {
                foreach (IncomingTransfer transfer in removedTransfers)
                {
                    incomingTransfers.Remove(transfer.Id);
                }
            }
        }

        return removedTransfers ?? [];
    }

    /// <summary>
    /// Clears all active incoming transfers with a contextual warning.
    /// </summary>
    /// <param name="reason">The reason for clearing transfers.</param>
    static void ClearIncomingTransfers(string reason)
    {
        List<IncomingTransfer> removedTransfers = null;

        lock (incomingLock)
        {
            if (incomingTransfers.Count == 0)
            {
                return;
            }

            removedTransfers = incomingTransfers.Values.ToList();
            incomingTransfers.Clear();
        }

        foreach (IncomingTransfer transfer in removedTransfers)
        {
            VWorld.Log.LogWarning(
                $"Incoming transfer cleared ~ ID: {transfer.Id} | Plugin: {transfer.FileName} | Reason: {reason}");
        }
    }

    /// <summary>
    /// Removes a client-side offer if it is pending.
    /// </summary>
    /// <param name="offerId">The offer identifier to remove.</param>
    /// <param name="offer">The removed offer.</param>
    /// <returns><c>true</c> if the offer was removed; otherwise, <c>false</c>.</returns>
    static bool TryRemoveClientOffer(Guid offerId, out TransferOffer offer)
    {
        CleanupExpiredOffers();

        lock (pendingOfferLock)
        {
            if (pendingClientOffers.TryGetValue(offerId, out var pendingOffer))
            {
                pendingClientOffers.Remove(offerId);
                offer = pendingOffer.Offer;
                return true;
            }
        }

        offer = default;
        return false;
    }

    /// <summary>
    /// Sends a decline message to the server for an expired offer.
    /// </summary>
    /// <param name="offerId">The offer identifier that expired.</param>
    static void TrySendDeclineForExpiredOffer(Guid offerId)
    {
        if (!VWorld.IsClient || !API.Shared.VNetwork.IsReady)
        {
            return;
        }

        API.Shared.VNetwork.SendToServer(new TransferDecline(offerId, TransferDeclineReason.OfferExpired));
    }
    static bool TryFinalizeIncomingTransfer(Guid transferId)
    {
        IncomingTransfer incoming;
        TransferComplete complete;
        bool ok;

        lock (incomingLock)
        {
            if (!incomingTransfers.TryGetValue(transferId, out incoming)
                || !incoming.HasCompletion)
            {
                return false;
            }

            if (!incoming.IsComplete)
            {
                return false;
            }

            complete = incoming.Completion;
            ok = incoming.Verify();
            if (ok)
            {
                incomingTransfers.Remove(transferId);
            }
        }

        if (!ok)
        {
            LogFailure(transferId);
            return true;
        }

        IncomingRoutine(complete, incoming).Run();
        return true;
    }

    static void LogFailure(Guid transferId)
    {
        VWorld.Log?.LogWarning($"Transfer failed! ({DateTime.Now:HH\\:mm\\:ss})");

        lock (incomingLock)
        {
            if (!incomingTransfers.TryGetValue(transferId, out var incoming))
            {
                VWorld.Log?.LogWarning($"No transfer session found for {transferId}.");
                return;
            }

            if (incoming.IsComplete == false)
            {
                VWorld.Log?.LogWarning(
                    $"{incoming.TotalBytes.PrettyBytes()} bytes expected, only {incoming.ReceivedBytes.PrettyBytes()} bytes received...");
            }

            if (!incoming.Verify())
            {
                VWorld.Log?.LogWarning("Didn't pass SHA-256 verification...");
            }

            // VNetwork.SendToServer(new FileAck(transferId, false));
            incomingTransfers.Remove(transferId);
        }
    }

    /// <summary>
    /// Enqueues transfer hash computation work for the specified payload.
    /// </summary>
    /// <param name="transferId">The transfer identifier to associate with the work.</param>
    /// <param name="payload">The payload to hash.</param>
    /// <param name="onComplete">Callback invoked when hashing is complete.</param>
    static void QueueTransferHashWork(Guid transferId, byte[] payload, Action<byte[]> onComplete)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (onComplete is null)
        {
            throw new ArgumentNullException(nameof(onComplete));
        }

        EnqueueTransferWorkItem(new TransferHashWorkItem(transferId, payload, onComplete));
    }
    static HotloadPluginResult LoadPlugin(string filePath)
        => TryLoadPlugin(filePath);

    static HotloadPluginResult TryLoadPlugin(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new(
                false,
                HotloadPluginStatus.FileMissing,
                $"Plugin file does not exist: {filePath}");
        }

        AssemblyName assemblyName;
        try
        {
            assemblyName = AssemblyName.GetAssemblyName(filePath);
        }
        catch (Exception ex)
        {
            return new(
                false,
                HotloadPluginStatus.Failed,
                $"Failed to read plugin assembly identity from {filePath}: {ex.Message}");
        }

        Assembly assembly;
        bool reusedLoadedAssembly;
        try
        {
            assembly = ResolveHotloadAssembly(filePath, assemblyName, out reusedLoadedAssembly);
        }
        catch (Exception ex)
        {
            return new(
                false,
                HotloadPluginStatus.Failed,
                $"Failed to load plugin assembly from {filePath}: {ex.Message}",
                AssemblyName: assemblyName.Name);
        }

        Type type = GetLoadableTypes(assembly)
            .FirstOrDefault(t => typeof(BasePlugin).IsAssignableFrom(t) && !t.IsAbstract);
        if (type is null)
        {
            return new(
                false,
                HotloadPluginStatus.PluginTypeMissing,
                $"Assembly {assemblyName.Name} does not contain a concrete BasePlugin type.",
                AssemblyName: assembly.GetName().Name);
        }

        BepInPlugin metadata = type.GetCustomAttribute<BepInPlugin>();
        if (metadata is null)
        {
            return new(
                false,
                HotloadPluginStatus.Failed,
                $"Failed to read BepInEx metadata for {type.FullName}.",
                AssemblyName: assembly.GetName().Name);
        }

        if (IsPluginGuidLoaded(metadata.GUID, reusedLoadedAssembly ? null : assembly))
        {
            return new(
                false,
                HotloadPluginStatus.PluginGuidAlreadyLoaded,
                $"Plugin GUID {metadata.GUID} is already loaded.",
                metadata.GUID,
                metadata.Name,
                metadata.Version.ToString(),
                assembly.GetName().Name);
        }

        BasePlugin plugin;
        try
        {
            plugin = (BasePlugin)Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Activator returned null.");
        }
        catch (Exception ex)
        {
            return new(
                false,
                HotloadPluginStatus.PluginCreateFailed,
                $"Failed to create plugin instance for {type.FullName}: {ex.Message}",
                AssemblyName: assembly.GetName().Name);
        }

        try
        {
            plugin.Load();
            Hotloader.ReflectAndInitialize(assembly);
            _hotloadedPluginGuids.Add(metadata.GUID);
        }
        catch (Exception ex)
        {
            return new(
                false,
                HotloadPluginStatus.Failed,
                $"Plugin {metadata.Name} failed during Load/Initialize: {ex}",
                metadata.GUID,
                metadata.Name,
                metadata.Version.ToString(),
                assembly.GetName().Name);
        }

        return new(
            true,
            HotloadPluginStatus.Loaded,
            $"Plugin {metadata.Name} {metadata.Version} loaded.",
            metadata.GUID,
            metadata.Name,
            metadata.Version.ToString(),
            assembly.GetName().Name);
    }

    static Assembly ResolveHotloadAssembly(
        string filePath,
        AssemblyName assemblyName,
        out bool reusedLoadedAssembly)
    {
        Assembly loadedAssembly = FindLoadedAssembly(assemblyName, filePath);
        if (loadedAssembly is not null)
        {
            reusedLoadedAssembly = true;
            return loadedAssembly;
        }

        reusedLoadedAssembly = false;
        return Assembly.LoadFrom(filePath);
    }

    static Assembly FindLoadedAssembly(AssemblyName assemblyName, string filePath)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            if (string.Equals(assembly.GetName().FullName, assemblyName.FullName, StringComparison.Ordinal))
            {
                return assembly;
            }
        }

        return null;
    }

    static bool IsPluginGuidLoaded(string pluginGuid, Assembly exceptAssembly = null)
    {
        if (_hotloadedPluginGuids.Contains(pluginGuid))
        {
            return true;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly == exceptAssembly)
            {
                continue;
            }

            foreach (Type type in GetLoadableTypes(assembly))
            {
                if (!typeof(BasePlugin).IsAssignableFrom(type) || type.IsAbstract)
                {
                    continue;
                }

                BepInPlugin metadata = type.GetCustomAttribute<BepInPlugin>();
                if (metadata is not null
                    && string.Equals(metadata.GUID, pluginGuid, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Select(type => type!);
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    static void LogHotloadPluginResult(string filePath, HotloadPluginResult result)
    {
        string fileName = Path.GetFileName(filePath);
        if (result.Success)
        {
            VWorld.Log?.LogInfo(
                $"[Hotload] Loaded {result.PluginName} {result.PluginVersion} " +
                $"({result.PluginGuid}) from {fileName}.");
            return;
        }

        VWorld.Log?.LogError(
            $"[Hotload] Failed to load {fileName}: {result.Status} - {result.Message}");
    }

    internal static HotloadPluginResult TryLoadPluginForTesting(string filePath)
        => TryLoadPlugin(filePath);

    internal static string FormatTransferStartMessageForTesting(string fileName, int totalBytes)
        => FormatTransferStartMessage(fileName, totalBytes);

    internal static string FormatTransferProgressMessageForTesting(
        string fileName,
        int receivedBytes,
        int totalBytes)
        => FormatTransferProgressMessage(fileName, receivedBytes, totalBytes);

    internal static bool ShouldLogTransferProgressForTesting(
        int receivedBytes,
        int totalBytes,
        int lastLoggedPercent,
        out int nextLoggedPercent)
        => ShouldLogTransferProgress(receivedBytes, totalBytes, lastLoggedPercent, out nextLoggedPercent);

    internal static string FormatTransferCompleteMessageForTesting(string fileName, int totalBytes)
        => FormatTransferCompleteMessage(fileName, totalBytes);

    internal static byte[] CompressBytesSynchronously(byte[] bytes)
    {
        byte[] result = null;
        TransferCompressionWorkItem workItem = new(Guid.Empty, bytes, output => result = output);

        while (!workItem.TryExecuteStep()) { }

        return result;
    }

    internal static Assembly ResolveHotloadAssemblyForTesting(string filePath, out bool reusedLoadedAssembly)
    {
        AssemblyName assemblyName = AssemblyName.GetAssemblyName(filePath);
        return ResolveHotloadAssembly(filePath, assemblyName, out reusedLoadedAssembly);
    }

    internal static Assembly FindLoadedAssemblyForTesting(AssemblyName assemblyName, string filePath)
        => FindLoadedAssembly(assemblyName, filePath);

    internal static bool IsPluginGuidLoadedForTesting(string pluginGuid)
        => IsPluginGuidLoaded(pluginGuid);

    public static IEnumerator CompressChunkRoutine(
        byte[] bytes,
        Action<byte[]> onComplete)
    {
        if (bytes is null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        if (onComplete is null)
        {
            throw new ArgumentNullException(nameof(onComplete));
        }

        byte[] Result = null;
        bool Completed = false;
        EnqueueTransferWorkItem(new TransferCompressionWorkItem(
            Guid.Empty,
            bytes,
            output =>
            {
                Result = output;
                Completed = true;
            }));

        while (!Completed)
        {
            yield return null;
        }

        onComplete(Result);
    }
    /// <summary>
    /// Decompresses a transfer buffer into incremental slices.
    /// </summary>
    /// <param name="compressedBuffer">The compressed payload buffer.</param>
    /// <param name="onSlice">Callback invoked for each decompressed slice.</param>
    /// <param name="onComplete">Callback invoked when decompression finishes.</param>
    /// <param name="transferId">The transfer identifier for work queue logging.</param>
    public static IEnumerator DecompressChunkRoutine(
        byte[] compressedBuffer,
        Action<byte[]> onSlice,
        Action onComplete,
        Guid transferId = default)
    {
        if (compressedBuffer is null)
        {
            throw new ArgumentNullException(nameof(compressedBuffer));
        }

        if (onSlice is null)
        {
            throw new ArgumentNullException(nameof(onSlice));
        }

        if (onComplete is null)
        {
            throw new ArgumentNullException(nameof(onComplete));
        }

        bool Completed = false;
        EnqueueTransferWorkItem(new TransferDecompressionWorkItem(
            transferId,
            compressedBuffer,
            onSlice,
            () => Completed = true));

        while (!Completed)
        {
            yield return null;
        }

        onComplete();
    }

    /// <summary>
    /// Calculates how many fixed-size chunks are required to transfer the specified byte count.
    /// </summary>
    /// <param name="totalBytes">The total bytes in the payload.</param>
    /// <returns>The expected number of chunks.</returns>
    static int CalculateExpectedChunkCount(int totalBytes)
    {
        if (totalBytes <= 0)
        {
            return 0;
        }

        return (totalBytes + Const.PACKET_BYTES - 1) / Const.PACKET_BYTES;
    }
    public class IncomingTransfer
    {
        public Guid Id;
        public int TotalBytes;
        public string FileName;
        public byte[] Sha = [];
        public byte[] ReleaseAssetSha = [];
        public int ExpectedChunkCount;
        public int ReceivedBytes { get; private set; }
        public int ReceivedChunkCount { get; private set; }
        int LastLoggedProgressPercent { get; set; }
        public bool IsComplete => ReceivedBytes >= TotalBytes && ReceivedChunkCount == ExpectedChunkCount;
        public bool HasCompletion { get; private set; }
        public TransferComplete Completion { get; private set; }
        /// <summary>
        /// Gets the UTC timestamp when the transfer was created.
        /// </summary>
        public DateTime StartedUtc { get; }
        /// <summary>
        /// Gets the UTC timestamp of the most recently received chunk.
        /// </summary>
        public DateTime LastChunkUtc { get; private set; }
        /// <summary>
        /// Gets the platform identifier for the transfer source.
        /// </summary>
        public ulong SourcePlatformId { get; }
        /// <summary>
        /// Gets whether a GitHub Release asset digest was provided for this transfer.
        /// </summary>
        public bool HasReleaseAssetDigest => Array.Exists(ReleaseAssetSha, value => value != 0);
        readonly byte[][] _chunks;
        public IncomingTransfer(TransferSession session, ulong sourcePlatformId)
        {
            Id = session.Id;
            FileName = session.FileNameString;
            TotalBytes = session.TotalBytes;
            Sha = session.Sha256Bytes;
            ReleaseAssetSha = session.ReleaseAssetSha256Bytes;
            ExpectedChunkCount = CalculateExpectedChunkCount(TotalBytes);
            _chunks = ExpectedChunkCount > 0 ? new byte[ExpectedChunkCount][] : Array.Empty<byte[]>();
            StartedUtc = GetUtcNow();
            LastChunkUtc = StartedUtc;
            SourcePlatformId = sourcePlatformId;
        }
        /// <summary>
        /// Records that the transfer sender has finished sending chunks.
        /// </summary>
        /// <param name="complete">The completion packet received from the sender.</param>
        public void MarkCompletionReceived(TransferComplete complete)
        {
            Completion = complete;
            HasCompletion = true;
        }
        /// <summary>
        /// Adds a chunk to the transfer when the index is valid and not already present.
        /// </summary>
        /// <param name="idx">The zero-based chunk index.</param>
        /// <param name="bytes">The chunk payload bytes.</param>
        public void AddChunk(int idx, byte[] bytes)
        {
            LastChunkUtc = GetUtcNow();

            if (idx < 0 || idx >= ExpectedChunkCount)
            {
                string expectedRange = ExpectedChunkCount > 0
                    ? $"0-{ExpectedChunkCount - 1}"
                    : "no chunks";
                VWorld.Log.LogWarning(
                    $"Rejecting chunk {idx} for transfer {Id} ({FileName}). Expected {expectedRange}.");
                return;
            }

            if (_chunks[idx] != null)
            {
                return;
            }

            int maxAllowedBytes = TotalBytes + Const.PACKET_BYTES;
            if (ReceivedBytes + bytes.Length > maxAllowedBytes)
            {
                VWorld.Log.LogWarning(
                    $"Rejecting chunk {idx} for transfer {Id} ({FileName}). " +
                    $"Accepting it would exceed {TotalBytes.PrettyBytes()} by more than one chunk.");
                return;
            }

            _chunks[idx] = bytes;
            ReceivedBytes += bytes.Length;
            ReceivedChunkCount++;

            if (ShouldLogTransferProgress(
                ReceivedBytes,
                TotalBytes,
                LastLoggedProgressPercent,
                out int nextLoggedProgressPercent))
            {
                LastLoggedProgressPercent = nextLoggedProgressPercent;
                VWorld.Log?.LogInfo(FormatTransferProgressMessage(FileName, ReceivedBytes, TotalBytes));
            }
        }
        /// <summary>
        /// Concatenates the ordered chunks into a single payload buffer.
        /// </summary>
        /// <returns>The full payload buffer.</returns>
        public byte[] Concat()
        {
            byte[] buffer = new byte[TotalBytes];

            for (int index = 0; index < _chunks.Length; index++)
            {
                byte[] chunk = _chunks[index];
                if (chunk == null)
                {
                    throw new InvalidOperationException(
                        $"Chunk {index} missing for transfer {Id} ({FileName}).");
                }

                int offset = index * Const.PACKET_BYTES;
                if (offset >= TotalBytes)
                {
                    throw new InvalidOperationException(
                        $"Chunk {index} exceeds expected payload size for transfer {Id} ({FileName}).");
                }

                int bytesToCopy = Math.Min(chunk.Length, TotalBytes - offset);
                Buffer.BlockCopy(chunk, 0, buffer, offset, bytesToCopy);
            }

            return buffer;
        }
        /// <summary>
        /// Verifies the transfer payload against the expected SHA-256 hash.
        /// </summary>
        /// <returns><c>true</c> when the hash matches; otherwise, <c>false</c>.</returns>
        public bool Verify()
        {
            if (!IsComplete)
            {
                return false;
            }

            using var sha = SHA256.Create();
            int processedBytes = 0;

            for (int index = 0; index < _chunks.Length; index++)
            {
                byte[] chunk = _chunks[index];
                if (chunk == null)
                {
                    return false;
                }

                int remainingBytes = TotalBytes - processedBytes;
                if (remainingBytes <= 0)
                {
                    break;
                }

                int bytesToHash = Math.Min(chunk.Length, remainingBytes);
                sha.TransformBlock(chunk, 0, bytesToHash, null, 0);
                processedBytes += bytesToHash;
            }

            if (processedBytes != TotalBytes)
            {
                return false;
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return sha.Hash != null && sha.Hash.SequenceEqual(Sha);
        }

        /// <summary>
        /// Validates the decompressed payload against the optional GitHub Release asset digest.
        /// </summary>
        /// <param name="decompressedBytes">The decompressed DLL or raw ZIP archive bytes.</param>
        /// <param name="isZip">Whether the payload represents a ZIP archive.</param>
        /// <param name="errorMessage">The error message when validation fails.</param>
        /// <returns>
        /// <c>true</c> when the hash matches or no GitHub Release asset digest is provided; otherwise, <c>false</c>.
        /// </returns>
        public bool TryValidateReleaseAsset(byte[] decompressedBytes, bool isZip, out string errorMessage)
        {
            if (!HasReleaseAssetDigest)
            {
                errorMessage = string.Empty;
                return true;
            }

            using SHA256 sha = SHA256.Create();
            byte[] computedHash = sha.ComputeHash(decompressedBytes);
            return TryValidateReleaseAssetDigest(computedHash, isZip, out errorMessage);
        }

        /// <summary>
        /// Validates a precomputed GitHub Release asset digest against the expected value.
        /// </summary>
        /// <param name="computedHash">The computed SHA-256 hash for the payload.</param>
        /// <param name="isZip">Whether the payload represents a ZIP archive.</param>
        /// <param name="errorMessage">The error message when validation fails.</param>
        /// <returns>
        /// <c>true</c> when the hash matches or no GitHub Release asset digest is provided; otherwise, <c>false</c>.
        /// </returns>
        public bool TryValidateReleaseAssetDigest(byte[] computedHash, bool isZip, out string errorMessage)
        {
            if (!HasReleaseAssetDigest)
            {
                errorMessage = string.Empty;
                return true;
            }

            if (computedHash is null)
            {
                errorMessage = "Transfer aborted: GitHub Release asset digest could not be computed.";
                return false;
            }

            if (computedHash.SequenceEqual(ReleaseAssetSha))
            {
                errorMessage = string.Empty;
                return true;
            }

            string payloadDescription = isZip ? "ZIP archive" : "DLL";
            string expectedHash = Convert.ToHexString(ReleaseAssetSha);
            string receivedHash = Convert.ToHexString(computedHash);

            errorMessage =
                $"Transfer aborted: GitHub Release asset digest mismatch for {FileName} ({payloadDescription}). " +
                $"Expected {expectedHash}, but received {receivedHash}.";
            return false;
        }
    }

    /// <summary>
    /// Represents the outcome of a queued file write operation.
    /// </summary>
    /// <param name="Success">Whether the write succeeded.</param>
    /// <param name="ErrorMessage">The error message when the write failed.</param>
    readonly record struct TransferFileWriteResult(bool Success, string ErrorMessage);

    /// <summary>
    /// Schedules compression work in fixed-size slices.
    /// </summary>
    sealed class TransferCompressionWorkItem : ITransferWorkItem
    {
        readonly byte[] bytes;
        readonly Action<byte[]> onComplete;
        MemoryStream combined;
        int offset;
        bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferCompressionWorkItem"/> class.
        /// </summary>
        /// <param name="transferId">The transfer identifier.</param>
        /// <param name="bytes">The raw bytes to compress.</param>
        /// <param name="onComplete">Callback invoked with the compressed bytes.</param>
        public TransferCompressionWorkItem(Guid transferId, byte[] bytes, Action<byte[]> onComplete)
        {
            TransferId = transferId;
            this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            this.onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            combined = new MemoryStream();
        }

        /// <summary>
        /// Gets the identifier for the transfer being processed.
        /// </summary>
        public Guid TransferId { get; }

        /// <summary>
        /// Executes a single compression step.
        /// </summary>
        /// <returns><c>true</c> when compression is complete; otherwise, <c>false</c>.</returns>
        public bool TryExecuteStep()
        {
            if (completed)
            {
                return true;
            }

            if (offset >= bytes.Length)
            {
                Complete();
                return true;
            }

            int SliceLength = Math.Min(Const.PACKET_BYTES, bytes.Length - offset);
            byte[] CompressedSlice;
            using (var slice = new MemoryStream(bytes, offset, SliceLength, writable: false))
            using (var sliceOut = new MemoryStream())
            {
                using (var brotli = new BrotliStream(
                    sliceOut,
                    System.IO.Compression.CompressionLevel.Fastest,
                    leaveOpen: true))
                {
                    slice.CopyTo(brotli);
                }

                CompressedSlice = sliceOut.ToArray();
            }

            combined.Write(BitConverter.GetBytes(CompressedSlice.Length));
            combined.Write(CompressedSlice, 0, CompressedSlice.Length);
            offset += SliceLength;

            if (offset >= bytes.Length)
            {
                Complete();
                return true;
            }

            return false;
        }

        void Complete()
        {
            completed = true;
            byte[] OutputBytes = combined.ToArray();
            combined.Dispose();
            combined = null;
            onComplete(OutputBytes);
        }
    }

    /// <summary>
    /// Schedules decompression work in fixed-size slices.
    /// </summary>
    sealed class TransferDecompressionWorkItem : ITransferWorkItem
    {
        readonly byte[] compressedBuffer;
        readonly Action<byte[]> onSlice;
        readonly Action onComplete;
        int offset;
        bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferDecompressionWorkItem"/> class.
        /// </summary>
        /// <param name="transferId">The transfer identifier.</param>
        /// <param name="compressedBuffer">The compressed buffer to decompress.</param>
        /// <param name="onSlice">Callback invoked for each decompressed slice.</param>
        /// <param name="onComplete">Callback invoked when decompression finishes.</param>
        public TransferDecompressionWorkItem(
            Guid transferId,
            byte[] compressedBuffer,
            Action<byte[]> onSlice,
            Action onComplete)
        {
            TransferId = transferId;
            this.compressedBuffer = compressedBuffer ?? throw new ArgumentNullException(nameof(compressedBuffer));
            this.onSlice = onSlice ?? throw new ArgumentNullException(nameof(onSlice));
            this.onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
        }

        /// <summary>
        /// Gets the identifier for the transfer being processed.
        /// </summary>
        public Guid TransferId { get; }

        /// <summary>
        /// Executes a single decompression step.
        /// </summary>
        /// <returns><c>true</c> when decompression is complete; otherwise, <c>false</c>.</returns>
        public bool TryExecuteStep()
        {
            if (completed)
            {
                return true;
            }

            if (offset >= compressedBuffer.Length)
            {
                Complete();
                return true;
            }

            if (offset + 4 > compressedBuffer.Length)
            {
                throw new InvalidDataException("Truncated length prefix.");
            }

            int SliceLength = BitConverter.ToInt32(compressedBuffer, offset);
            offset += 4;

            if (offset + SliceLength > compressedBuffer.Length)
            {
                throw new InvalidDataException("Truncated compressed slice.");
            }

            byte[] decompressedSlice;
            using (var sliceIn = new MemoryStream(compressedBuffer, offset, SliceLength, writable: false))
            using (var sliceOut = new MemoryStream())
            using (var brotli = new BrotliStream(sliceIn, CompressionMode.Decompress))
            {
                brotli.CopyTo(sliceOut);
                decompressedSlice = sliceOut.ToArray();
            }

            onSlice(decompressedSlice);

            offset += SliceLength;

            if (offset >= compressedBuffer.Length)
            {
                Complete();
                return true;
            }

            return false;
        }

        void Complete()
        {
            completed = true;
            onComplete();
        }
    }

    /// <summary>
    /// Schedules SHA-256 hashing work in fixed-size slices.
    /// </summary>
    sealed class TransferHashWorkItem : ITransferWorkItem
    {
        readonly byte[] payload;
        readonly Action<byte[]> onComplete;
        SHA256 sha;
        int offset;
        bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferHashWorkItem"/> class.
        /// </summary>
        /// <param name="transferId">The transfer identifier.</param>
        /// <param name="payload">The payload to hash.</param>
        /// <param name="onComplete">Callback invoked with the hash bytes.</param>
        public TransferHashWorkItem(Guid transferId, byte[] payload, Action<byte[]> onComplete)
        {
            TransferId = transferId;
            this.payload = payload ?? throw new ArgumentNullException(nameof(payload));
            this.onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
            sha = SHA256.Create();
        }

        /// <summary>
        /// Gets the identifier for the transfer being processed.
        /// </summary>
        public Guid TransferId { get; }

        /// <summary>
        /// Executes a single hashing step.
        /// </summary>
        /// <returns><c>true</c> when hashing is complete; otherwise, <c>false</c>.</returns>
        public bool TryExecuteStep()
        {
            if (completed)
            {
                return true;
            }

            if (payload.Length == 0 || offset >= payload.Length)
            {
                Complete();
                return true;
            }

            int BytesToHash = Math.Min(Const.PACKET_BYTES, payload.Length - offset);
            sha.TransformBlock(payload, offset, BytesToHash, null, 0);
            offset += BytesToHash;

            if (offset >= payload.Length)
            {
                Complete();
                return true;
            }

            return false;
        }

        void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            byte[] Hash = sha.Hash ?? [];
            sha.Dispose();
            sha = null;
            onComplete(Hash);
        }
    }

    /// <summary>
    /// Schedules chunk sending work for an outgoing transfer.
    /// </summary>
    sealed class TransferChunkSendWorkItem : ITransferWorkItem
    {
        readonly User target;
        readonly byte[] fileBytes;
        readonly ushort chunkSize;
        readonly bool hotload;
        readonly Action onComplete;
        int offset;
        int index;
        bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferChunkSendWorkItem"/> class.
        /// </summary>
        /// <param name="transferId">The transfer identifier.</param>
        /// <param name="target">The target user.</param>
        /// <param name="fileBytes">The payload bytes.</param>
        /// <param name="chunkSize">The chunk size to send.</param>
        /// <param name="hotload">Whether to hotload after completion.</param>
        /// <param name="onComplete">Callback invoked when all chunks are sent.</param>
        public TransferChunkSendWorkItem(
            Guid transferId,
            User target,
            byte[] fileBytes,
            ushort chunkSize,
            bool hotload,
            Action onComplete)
        {
            TransferId = transferId;
            this.target = target;
            this.fileBytes = fileBytes ?? throw new ArgumentNullException(nameof(fileBytes));
            this.chunkSize = chunkSize;
            this.hotload = hotload;
            this.onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
        }

        /// <summary>
        /// Gets the identifier for the transfer being processed.
        /// </summary>
        public Guid TransferId { get; }

        /// <summary>
        /// Executes a single chunk send step.
        /// </summary>
        /// <returns><c>true</c> when sending is complete; otherwise, <c>false</c>.</returns>
        public bool TryExecuteStep()
        {
            if (completed)
            {
                return true;
            }

            if (offset >= fileBytes.Length)
            {
                API.Shared.VNetwork.SendToClient(target, new TransferComplete(TransferId, hotload));
                completed = true;
                onComplete();
                return true;
            }

            int ChunkLength = Math.Min(chunkSize, fileBytes.Length - offset);
            byte[] ChunkBytes = new byte[ChunkLength];
            Buffer.BlockCopy(fileBytes, offset, ChunkBytes, 0, ChunkLength);

            API.Shared.VNetwork.SendToClient(target, new TransferChunk(TransferId, index, ChunkBytes));

            offset += ChunkLength;
            index++;
            return false;
        }
    }

    /// <summary>
    /// Schedules chunk receive work for an incoming transfer.
    /// </summary>
    sealed class TransferChunkReceiveWorkItem : ITransferWorkItem
    {
        readonly int index;
        readonly byte[] bytes;
        bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferChunkReceiveWorkItem"/> class.
        /// </summary>
        /// <param name="transferId">The transfer identifier.</param>
        /// <param name="index">The chunk index.</param>
        /// <param name="bytes">The chunk payload bytes.</param>
        public TransferChunkReceiveWorkItem(Guid transferId, int index, byte[] bytes)
        {
            TransferId = transferId;
            this.index = index;
            this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        }

        /// <summary>
        /// Gets the identifier for the transfer being processed.
        /// </summary>
        public Guid TransferId { get; }

        /// <summary>
        /// Executes a single chunk receive step.
        /// </summary>
        /// <returns><c>true</c> when the chunk is processed; otherwise, <c>false</c>.</returns>
        public bool TryExecuteStep()
        {
            if (completed)
            {
                return true;
            }

            bool shouldFinalize = false;
            lock (incomingLock)
            {
                if (incomingTransfers.TryGetValue(TransferId, out var incoming))
                {
                    incoming.AddChunk(index, bytes);
                    shouldFinalize = incoming.HasCompletion && incoming.IsComplete;
                }
            }

            if (shouldFinalize)
            {
                TryFinalizeIncomingTransfer(TransferId);
            }

            completed = true;
            return true;
        }
    }

    /// <summary>
    /// Schedules incoming transfer completion after queued chunk receive work.
    /// </summary>
    sealed class TransferCompleteWorkItem : ITransferWorkItem
    {
        readonly TransferComplete complete;
        bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferCompleteWorkItem"/> class.
        /// </summary>
        /// <param name="complete">The transfer completion packet.</param>
        public TransferCompleteWorkItem(TransferComplete complete)
        {
            this.complete = complete;
        }

        /// <summary>
        /// Gets the transfer identifier.
        /// </summary>
        public Guid TransferId => complete.Id;

        /// <summary>
        /// Executes the completion step.
        /// </summary>
        /// <returns><c>true</c> after completion processing runs.</returns>
        public bool TryExecuteStep()
        {
            if (completed)
            {
                return true;
            }

            ProcessTransferComplete(complete);
            completed = true;
            return true;
        }
    }

    /// <summary>
    /// Schedules file write work for a transfer.
    /// </summary>
    sealed class TransferFileWriteWorkItem : ITransferWorkItem
    {
        readonly string filePath;
        readonly byte[] bytes;
        readonly Action<TransferFileWriteResult> onComplete;
        FileStream outputStream;
        int offset;
        bool completed;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferFileWriteWorkItem"/> class.
        /// </summary>
        /// <param name="transferId">The transfer identifier.</param>
        /// <param name="filePath">The destination file path.</param>
        /// <param name="bytes">The bytes to write.</param>
        /// <param name="onComplete">Callback invoked with the write result.</param>
        public TransferFileWriteWorkItem(Guid transferId, string filePath, byte[] bytes, Action<TransferFileWriteResult> onComplete)
        {
            TransferId = transferId;
            this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            this.onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
        }

        /// <summary>
        /// Gets the identifier for the transfer being processed.
        /// </summary>
        public Guid TransferId { get; }

        /// <summary>
        /// Executes the file write step.
        /// </summary>
        /// <returns><c>true</c> when the write is complete; otherwise, <c>false</c>.</returns>
        public bool TryExecuteStep()
        {
            if (completed)
            {
                return true;
            }

            try
            {
                EnsureOutputStream();

                int remainingBytes = bytes.Length - offset;
                if (remainingBytes <= 0)
                {
                    CompleteSuccess();
                    return true;
                }

                int bytesToWrite = Math.Min(Const.PACKET_BYTES, remainingBytes);
                outputStream.Write(bytes, offset, bytesToWrite);
                offset += bytesToWrite;

                if (offset >= bytes.Length)
                {
                    CompleteSuccess();
                    return true;
                }
            }
            catch (Exception ex)
            {
                CompleteFailure(ex.Message);
                return true;
            }

            return false;
        }

        void EnsureOutputStream()
        {
            outputStream ??= new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        }

        void CompleteSuccess()
        {
            completed = true;
            outputStream?.Dispose();
            outputStream = null;
            onComplete(new TransferFileWriteResult(true, string.Empty));
        }

        void CompleteFailure(string message)
        {
            completed = true;
            outputStream?.Dispose();
            outputStream = null;
            onComplete(new TransferFileWriteResult(false, message));
        }
    }

    class PendingClientOffer(TransferOffer offer, DateTime expiresAt)
    {
        public TransferOffer Offer { get; } = offer;
        public DateTime ExpiresAt { get; } = expiresAt;
    }

    class PendingServerOffer(TransferOffer offer, TransferRequest request, ulong targetId, DateTime expiresAt)
    {
        public TransferOffer Offer { get; } = offer;
        public TransferRequest Request { get; } = request;
        public ulong TargetId { get; } = targetId;
        public DateTime ExpiresAt { get; } = expiresAt;
    }

    /// <summary>
    /// Builds a temporary file path for an incoming transfer payload.
    /// </summary>
    /// <param name="fileName">The incoming file name.</param>
    /// <param name="transferId">The transfer identifier.</param>
    /// <returns>The temporary file path to use during streaming writes.</returns>
    static string BuildIncomingTempFilePath(string fileName, Guid transferId)
    {
        string normalizedFileName = string.IsNullOrWhiteSpace(fileName)
            ? "transfer"
            : Path.GetFileName(fileName);
        return Path.Combine(Paths.PluginPath, $"{normalizedFileName}.{transferId:N}.download");
    }

    /// <summary>
    /// Deletes a temporary file if it exists.
    /// </summary>
    /// <param name="filePath">The temporary file path.</param>
    static void DeleteTemporaryFileIfPresent(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
            VWorld.Log.LogWarning($"Deleted temporary transfer file: {filePath}");
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"Failed to delete temporary transfer file {filePath}: {ex}");
        }
    }

    /// <summary>
    /// Moves a validated temporary file into its final destination.
    /// </summary>
    /// <param name="tempFilePath">The temporary file path.</param>
    /// <param name="destinationPath">The final destination path.</param>
    /// <param name="allowOverwrite">Whether overwrite was approved.</param>
    static void FinalizeIncomingFile(string tempFilePath, string destinationPath, bool allowOverwrite)
    {
        if (string.IsNullOrWhiteSpace(tempFilePath))
        {
            throw new ArgumentException("Temporary file path is required.", nameof(tempFilePath));
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination file path is required.", nameof(destinationPath));
        }

        if (!File.Exists(tempFilePath))
        {
            throw new FileNotFoundException("Temporary transfer file is missing.", tempFilePath);
        }

        if (File.Exists(destinationPath))
        {
            if (!allowOverwrite)
            {
                throw new InvalidOperationException($"Overwrite not permitted for {destinationPath}.");
            }

            File.Delete(destinationPath);
        }

        File.Move(tempFilePath, destinationPath);
    }

    /// <summary>
    /// Records overwrite confirmation for a specific transfer offer and file name.
    /// </summary>
    /// <param name="transferId">The accepted offer or transfer identifier.</param>
    /// <param name="fileName">The file name approved for overwrite.</param>
    static void RegisterOverwriteConfirmation(Guid transferId, string fileName)
    {
        if (transferId == Guid.Empty || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        DateTime expiresAt = GetUtcNow().Add(offerTimeout);
        var key = new OverwriteConfirmationKey(transferId, fileName);

        lock (overwriteConfirmationLock)
        {
            overwriteConfirmations[key] = expiresAt;
        }
    }

    /// <summary>
    /// Consumes overwrite confirmation for a specific transfer and file name if present.
    /// </summary>
    /// <param name="transferId">The accepted offer or transfer identifier.</param>
    /// <param name="fileName">The file name to check for confirmation.</param>
    /// <returns><c>true</c> if confirmation was present and consumed; otherwise, <c>false</c>.</returns>
    static bool TryConsumeOverwriteConfirmation(Guid transferId, string fileName)
    {
        if (transferId == Guid.Empty || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var key = new OverwriteConfirmationKey(transferId, fileName);

        lock (overwriteConfirmationLock)
        {
            if (!overwriteConfirmations.TryGetValue(key, out DateTime expiresAt))
            {
                return false;
            }

            if (expiresAt <= GetUtcNow())
            {
                overwriteConfirmations.Remove(key);
                return false;
            }

            overwriteConfirmations.Remove(key);
            return true;
        }
    }

    /// <summary>
    /// Removes expired overwrite confirmations.
    /// </summary>
    /// <param name="now">The current UTC timestamp.</param>
    static void CleanupExpiredOverwriteConfirmations(DateTime now)
    {
        List<OverwriteConfirmationKey> expiredEntries = null;

        lock (overwriteConfirmationLock)
        {
            foreach (var entry in overwriteConfirmations)
            {
                if (entry.Value <= now)
                {
                    expiredEntries ??= [];
                    expiredEntries.Add(entry.Key);
                }
            }

            if (expiredEntries != null)
            {
                foreach (OverwriteConfirmationKey key in expiredEntries)
                {
                    overwriteConfirmations.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// Records overwrite confirmation for testing.
    /// </summary>
    /// <param name="transferId">The accepted offer or transfer identifier.</param>
    /// <param name="fileName">The file name approved for overwrite.</param>
    internal static void RegisterOverwriteConfirmationForTesting(Guid transferId, string fileName)
    {
        RegisterOverwriteConfirmation(transferId, fileName);
    }

    /// <summary>
    /// Consumes overwrite confirmation for testing.
    /// </summary>
    /// <param name="transferId">The accepted offer or transfer identifier.</param>
    /// <param name="fileName">The file name to check for confirmation.</param>
    /// <returns><c>true</c> if confirmation was present and consumed; otherwise, <c>false</c>.</returns>
    internal static bool TryConsumeOverwriteConfirmationForTesting(Guid transferId, string fileName)
    {
        return TryConsumeOverwriteConfirmation(transferId, fileName);
    }

    /// <summary>
    /// Clears overwrite confirmations for testing.
    /// </summary>
    internal static void ResetOverwriteConfirmationsForTesting()
    {
        lock (overwriteConfirmationLock)
        {
            overwriteConfirmations.Clear();
        }
    }

    readonly struct OverwriteConfirmationKey : IEquatable<OverwriteConfirmationKey>
    {
        readonly Guid transferId;
        readonly string fileName;

        public OverwriteConfirmationKey(Guid transferId, string fileName)
        {
            this.transferId = transferId;
            this.fileName = fileName ?? string.Empty;
        }

        public bool Equals(OverwriteConfirmationKey other)
            => transferId == other.transferId
            && string.Equals(fileName, other.fileName, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj)
            => obj is OverwriteConfirmationKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(transferId, StringComparer.OrdinalIgnoreCase.GetHashCode(fileName));
    }

    /// <summary>
    /// Handles overwrite checks for an incoming DLL transfer.
    /// </summary>
    /// <param name="filePath">The destination path for the DLL.</param>
    /// <param name="incoming">The incoming transfer metadata.</param>
    /// <param name="allowOverwrite">Whether overwrite confirmation was provided.</param>
    /// <returns><c>true</c> if the write should proceed; otherwise, <c>false</c>.</returns>
    static bool TryHandleExistingDll(string filePath, IncomingTransfer incoming, bool allowOverwrite)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        if (!File.Exists(filePath))
        {
            return true;
        }

        if (incoming.HasReleaseAssetDigest && TryGetFileSha256(filePath, out byte[] existingHash))
        {
            if (existingHash.SequenceEqual(incoming.ReleaseAssetSha))
            {
                VWorld.Log.LogWarning(
                    $"Skipping {incoming.FileName}: GitHub Release asset digest matches installed DLL.");
                return false;
            }
        }

        if (!allowOverwrite)
        {
            VWorld.Log.LogWarning(
                $"Skipping {incoming.FileName}: existing DLL found. " +
                "Re-accept the transfer with overwrite confirmation to replace it.");
            return false;
        }

        VWorld.Log.LogWarning($"Overwriting existing DLL for {incoming.FileName}.");
        return true;
    }

    /// <summary>
    /// Checks for extraction collisions in a ZIP archive and logs user-facing messages.
    /// </summary>
    /// <param name="destinations">The validated archive destinations.</param>
    /// <param name="archiveName">The archive name for logging context.</param>
    /// <param name="allowOverwrite">Whether overwrite confirmation was provided.</param>
    /// <returns><c>true</c> if extraction should proceed; otherwise, <c>false</c>.</returns>
    static bool TryHandleZipCollisions(IReadOnlyList<ZipEntryDestination> destinations, string archiveName, bool allowOverwrite)
    {
        List<string> collisions = GetZipCollisionPaths(destinations);
        if (collisions.Count == 0)
        {
            return true;
        }

        string collisionSummary = FormatCollisionSummary(collisions);

        if (!allowOverwrite)
        {
            VWorld.Log.LogWarning(
                $"Skipping extraction of {archiveName}: existing paths would be overwritten ({collisionSummary}). " +
                "Re-accept the transfer with overwrite confirmation to replace them.");
            return false;
        }

        VWorld.Log.LogWarning(
            $"Overwriting existing files while extracting {archiveName}: {collisionSummary}.");
        return true;
    }

    /// <summary>
    /// Formats a collision list for logging.
    /// </summary>
    /// <param name="collisions">The collision paths to format.</param>
    /// <returns>A formatted summary string.</returns>
    static string FormatCollisionSummary(IReadOnlyCollection<string> collisions)
    {
        int count = collisions.Count;
        IEnumerable<string> sample = collisions.Take(MAX_COLLISION_LOG_COUNT);
        string detail = string.Join(", ", sample);
        return count > MAX_COLLISION_LOG_COUNT
            ? $"{detail}, +{count - MAX_COLLISION_LOG_COUNT} more"
            : detail;
    }

    /// <summary>
    /// Attempts to compute a SHA-256 hash for the specified file.
    /// </summary>
    /// <param name="filePath">The file path to hash.</param>
    /// <param name="hash">The computed hash bytes.</param>
    /// <returns><c>true</c> if the hash was computed; otherwise, <c>false</c>.</returns>
    static bool TryGetFileSha256(string filePath, out byte[] hash)
    {
        hash = [];

        try
        {
            using SHA256 sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            hash = sha.ComputeHash(stream);
            return true;
        }
        catch (Exception ex)
        {
            VWorld.Log.LogWarning($"Failed to hash existing file {filePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Extracts a ZIP archive into the specified root path while rejecting entries that escape the root.
    /// </summary>
    /// <param name="archive">The ZIP archive to extract.</param>
    /// <param name="rootPath">The intended root path for extracted files.</param>
    /// <param name="archiveName">The name of the archive for logging context.</param>
    /// <returns><c>true</c> when extraction succeeded; otherwise, <c>false</c>.</returns>
    static bool TryExtractZipArchive(ZipArchive archive, string rootPath, string archiveName)
    {
        if (!TryGetZipEntryDestinations(archive, rootPath, archiveName, out var destinations))
        {
            return false;
        }

        return TryExtractZipArchive(destinations);
    }

    /// <summary>
    /// Extracts a ZIP archive to the pre-validated destination list.
    /// </summary>
    /// <param name="destinations">The validated archive destinations.</param>
    /// <returns><c>true</c> when extraction succeeded; otherwise, <c>false</c>.</returns>
    static bool TryExtractZipArchive(IReadOnlyList<ZipEntryDestination> destinations)
    {
        foreach (var destination in destinations)
        {
            if (destination.IsDirectory)
            {
                Directory.CreateDirectory(destination.DestinationPath);
                continue;
            }

            string destinationDirectory = Path.GetDirectoryName(destination.DestinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            using var entryStream = destination.Entry.Open();
            using var outputStream = File.Open(destination.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(outputStream);
        }

        return true;
    }

    /// <summary>
    /// Extracts a ZIP archive to the pre-validated destination list while yielding between entries.
    /// </summary>
    /// <param name="destinations">The validated archive destinations.</param>
    /// <returns>An enumerator for coroutine execution.</returns>
    static IEnumerator ExtractZipArchiveRoutine(IReadOnlyList<ZipEntryDestination> destinations)
    {
        foreach (var destination in destinations)
        {
            if (destination.IsDirectory)
            {
                Directory.CreateDirectory(destination.DestinationPath);
            }
            else
            {
                string destinationDirectory = Path.GetDirectoryName(destination.DestinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                using var entryStream = destination.Entry.Open();
                using var outputStream = File.Open(destination.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                entryStream.CopyTo(outputStream);
            }

            yield return null;
        }
    }

    /// <summary>
    /// Validates ZIP entries and builds their destination paths.
    /// </summary>
    /// <param name="archive">The ZIP archive to inspect.</param>
    /// <param name="rootPath">The intended root path for extracted files.</param>
    /// <param name="archiveName">The name of the archive for logging context.</param>
    /// <param name="destinations">The validated destination list.</param>
    /// <returns><c>true</c> if all entries were valid; otherwise, <c>false</c>.</returns>
    static bool TryGetZipEntryDestinations(
        ZipArchive archive,
        string rootPath,
        string archiveName,
        out List<ZipEntryDestination> destinations)
    {
        string normalizedRootPath = NormalizeRootPath(rootPath);
        destinations = new List<ZipEntryDestination>(archive.Entries.Count);

        foreach (var entry in archive.Entries)
        {
            if (Path.IsPathRooted(entry.FullName))
            {
                VWorld.Log.LogError($"Zip entry is an absolute path and was rejected: {archiveName} -> {entry.FullName}");
                return false;
            }

            string destinationPath = Path.GetFullPath(Path.Combine(normalizedRootPath, entry.FullName));

            if (!destinationPath.StartsWith(normalizedRootPath, StringComparison.OrdinalIgnoreCase))
            {
                VWorld.Log.LogError($"Zip entry escapes the root and was rejected: {archiveName} -> {entry.FullName}");
                return false;
            }

            bool isDirectory = IsDirectoryEntry(entry);
            destinations.Add(new ZipEntryDestination(entry, destinationPath, isDirectory));
        }

        return true;
    }

    /// <summary>
    /// Identifies existing files or directories that would be overwritten by a ZIP extract.
    /// </summary>
    /// <param name="destinations">The validated archive destinations.</param>
    /// <returns>The collection of colliding paths.</returns>
    static List<string> GetZipCollisionPaths(IReadOnlyList<ZipEntryDestination> destinations)
    {
        List<string> collisions = [];

        foreach (var destination in destinations)
        {
            if (destination.IsDirectory)
            {
                if (File.Exists(destination.DestinationPath))
                {
                    collisions.Add(destination.DestinationPath);
                }

                continue;
            }

            if (File.Exists(destination.DestinationPath) || Directory.Exists(destination.DestinationPath))
            {
                collisions.Add(destination.DestinationPath);
            }
        }

        return collisions;
    }

    /// <summary>
    /// Normalizes the root path to a full path with a trailing directory separator.
    /// </summary>
    /// <param name="rootPath">The root path to normalize.</param>
    /// <returns>The normalized root path.</returns>
    static string NormalizeRootPath(string rootPath)
    {
        string fullPath = Path.GetFullPath(rootPath);
        return fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : $"{fullPath}{Path.DirectorySeparatorChar}";
    }

    /// <summary>
    /// Determines whether the ZIP entry represents a directory.
    /// </summary>
    /// <param name="entry">The ZIP entry to inspect.</param>
    /// <returns><c>true</c> when the entry is a directory; otherwise, <c>false</c>.</returns>
    static bool IsDirectoryEntry(ZipArchiveEntry entry)
    {
        return string.IsNullOrEmpty(entry.Name)
            || entry.FullName.EndsWith("/", StringComparison.Ordinal)
            || entry.FullName.EndsWith("\\", StringComparison.Ordinal);
    }

    readonly struct ZipEntryDestination(ZipArchiveEntry entry, string destinationPath, bool isDirectory)
    {
        public ZipArchiveEntry Entry { get; } = entry;
        public string DestinationPath { get; } = destinationPath;
        public bool IsDirectory { get; } = isDirectory;
    }

    readonly struct ReleaseDigestVerificationResult(
        bool shouldAbort,
        string releaseDigest,
        string releaseTag,
        byte[] releaseDigestBytes)
    {
        public readonly bool ShouldAbort = shouldAbort;
        public readonly string ReleaseDigest = releaseDigest;
        public readonly string ReleaseTag = releaseTag;
        public readonly byte[] ReleaseDigestBytes = releaseDigestBytes;
    }

    readonly struct ClientModLookup(HashSet<string> fileNames, HashSet<string> hashes)
    {
        public HashSet<string> FileNames { get; } = fileNames;
        public HashSet<string> Hashes { get; } = hashes;
    }

    /// <summary>
    /// Sends the current server-advertised shared-mod catalog to the client.
    /// </summary>
    /// <param name="user">The target user.</param>
    static void SendSharedClientModCatalog(User user)
    {
        if (!VWorld.IsServer)
        {
            return;
        }

        SharedClientModPreviewEntry[] entries =
            BuildSharedClientModCatalogEntries(GetServerShareEntries());

        VWorld.Log.LogInfo(
            $"[VShare] Sending server shared-mod catalog with {entries.Length} entries to {user.PlatformId}.");
        SendSharedClientModPreview(user, entries);
    }

    /// <summary>
    /// Sends the current shared-mod catalog preview to the client.
    /// </summary>
    /// <param name="user">The target user.</param>
    /// <param name="entries">The preview entries to show.</param>
    static void SendSharedClientModPreview(User user, SharedClientModPreviewEntry[] entries)
    {
        API.Shared.VNetwork.SendToClient(user, new SharedClientModPreview(entries));
    }

    /// <summary>
    /// Builds client-facing catalog entries from server-staged shared mods.
    /// </summary>
    /// <param name="sharedEntries">The server-staged shared mods.</param>
    /// <returns>The catalog entries to advertise to the client.</returns>
    static SharedClientModPreviewEntry[] BuildSharedClientModCatalogEntries(IReadOnlyList<SharedModEntry> sharedEntries)
        => BuildSharedClientModCatalogEntries(sharedEntries, TryGetClientShareMetadata);

    static SharedClientModPreviewEntry[] BuildSharedClientModCatalogEntries(
        IReadOnlyList<SharedModEntry> sharedEntries,
        TryGetClientShareMetadataDelegate tryGetMetadata)
    {
        if (sharedEntries is null || sharedEntries.Count == 0)
        {
            return [];
        }

        var entries = new List<SharedClientModPreviewEntry>(sharedEntries.Count);
        foreach (SharedModEntry entry in sharedEntries)
        {
            if (!tryGetMetadata(
                    entry,
                    out PluginShareMetadataStore.PluginShareMetadata metadata,
                    out string skipReason))
            {
                VWorld.Log?.LogWarning($"Skipping shared mod {entry.FileName}: {skipReason}");
                continue;
            }

            if (!IsFileNameWithinLimit(entry.FileName))
            {
                VWorld.Log?.LogWarning($"Skipping shared mod {entry.FileName}: file name exceeds packet length.");
                continue;
            }

            bool hotload = IsSharedModHotloadAllowed(entry.IsZip, metadata);
            entries.Add(CreateSharedClientModCatalogEntry(entry, metadata, hotload));
        }

        return entries.ToArray();
    }

    /// <summary>
    /// Creates a client-facing catalog entry from a server-staged mod.
    /// </summary>
    /// <param name="entry">The server-staged mod entry.</param>
    /// <param name="metadata">The resolved share metadata.</param>
    /// <param name="hotload">Whether runtime load is enabled for the mod.</param>
    /// <returns>The catalog entry to send to the client.</returns>
    static SharedClientModPreviewEntry CreateSharedClientModCatalogEntry(
        SharedModEntry entry,
        PluginShareMetadataStore.PluginShareMetadata metadata,
        bool hotload)
        => new(
            string.IsNullOrWhiteSpace(entry.MetadataBaseName)
                ? entry.BaseName
                : entry.MetadataBaseName,
            metadata.GitHubRepo,
            metadata.GitHubTag,
            entry.FileName,
            entry.IsZip,
            hotload);

    /// <summary>
    /// Builds the VShare request-button detail text from the latest server preview.
    /// </summary>
    /// <param name="entries">The latest preview entries, or <c>null</c> before a server check.</param>
    /// <returns>The options-menu detail text.</returns>
    static string BuildSharedModsMenuDescription(IReadOnlyList<SharedClientModPreviewEntry> entries)
    {
        if (entries is null)
        {
            return SHARED_MODS_MENU_HEADER +
                "\n- Waiting for server details.";
        }

        if (entries.Count == 0)
        {
            return SHARED_MODS_MENU_HEADER +
                "\n- No server-shared mods advertised by this server.";
        }

        var builder = new StringBuilder(SHARED_MODS_MENU_HEADER);

        int entryCount = Math.Min(entries.Count, MAX_SHARED_MOD_PREVIEW_ENTRIES);
        for (int i = 0; i < entryCount; i++)
        {
            SharedClientModPreviewEntry entry = entries[i];
            string displayName = SanitizePreviewText(entry.DisplayName, "Unknown mod");
            string tag = SanitizePreviewText(entry.GitHubTag, "unknown version");
            string repo = SanitizePreviewText(entry.GitHubRepo, "unknown repository");
            string fileName = SanitizePreviewText(entry.FileName, entry.IsZip ? "unknown.zip" : "unknown.dll");
            string fileType = entry.IsZip ? "ZIP" : "DLL";
            string runtimeLoad = entry.Hotload ? "enabled" : "disabled";

            builder.AppendLine();
            builder.Append("- ");
            builder.Append(displayName);
            builder.Append(' ');
            builder.Append(tag);
            builder.AppendLine();
            builder.Append("  Source: ");
            builder.AppendLine(repo);
            builder.Append("  File: ");
            builder.Append(fileName);
            builder.Append(" (");
            builder.Append(fileType);
            builder.AppendLine(")");
            builder.Append("  Runtime load: ");
            builder.AppendLine(runtimeLoad);
        }

        if (entries.Count > entryCount)
        {
            builder.Append("- ");
            builder.Append(entries.Count - entryCount);
            builder.AppendLine(" more server-shared mods available.");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Exposes the VShare menu description formatter to focused tests.
    /// </summary>
    /// <param name="entries">The preview entries to format.</param>
    /// <returns>The formatted options-menu detail text.</returns>
    internal static string BuildSharedModsMenuDescriptionForTesting(IReadOnlyList<SharedClientModPreviewEntry> entries)
        => BuildSharedModsMenuDescription(entries);

    internal static SharedClientModPreviewEntry[] BuildSharedClientModCatalogEntriesForTesting(
        IReadOnlyList<(string FileName, string BaseName, string MetadataBaseName, string Sha256, bool IsZip)> stagedEntries,
        IReadOnlyDictionary<string, PluginShareMetadataStore.PluginShareMetadata> metadataEntries)
    {
        SharedModEntry[] sharedEntries = stagedEntries
            .Select(entry => new SharedModEntry(
                entry.FileName,
                entry.BaseName,
                entry.MetadataBaseName,
                entry.Sha256,
                entry.IsZip))
            .ToArray();

        return BuildSharedClientModCatalogEntries(
            sharedEntries,
            (SharedModEntry entry, out PluginShareMetadataStore.PluginShareMetadata metadata, out string skipReason) =>
                TrySelectClientShareMetadata(
                    GetShareMetadataKeys(entry),
                    (string metadataKey, out PluginShareMetadataStore.PluginShareMetadata candidate, out string errorMessage) =>
                    {
                        if (metadataEntries.TryGetValue(metadataKey, out candidate))
                        {
                            errorMessage = string.Empty;
                            return true;
                        }

                        errorMessage = $"Share metadata was not found for '{metadataKey}'.";
                        return false;
                    },
                    out metadata,
                    out skipReason));
    }

    static void ArmSharedModAutoAcceptWindow(IReadOnlyList<SharedClientModPreviewEntry> entries)
    {
        lock (pendingOfferLock)
        {
            _sharedModAutoAcceptFileNames.Clear();
            if (entries is not null)
            {
                foreach (SharedClientModPreviewEntry entry in entries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.FileName))
                    {
                        _sharedModAutoAcceptFileNames.Add(entry.FileName);
                    }
                }
            }

            _sharedModAutoAcceptUntilUtc = GetUtcNow().AddSeconds(SHARED_MOD_AUTO_ACCEPT_WINDOW_SECONDS);
        }

        VWorld.Log.LogInfo(
            $"[VShare] Armed shared-mod auto-accept window for {_sharedModAutoAcceptFileNames.Count} catalog entries.");
    }

    static void TryAutoAcceptSharedModOffer(TransferOffer offer)
    {
        if (!ShouldAutoAcceptSharedModOffer(offer.FileNameString, GetUtcNow()))
        {
            return;
        }

        VWorld.Log.LogInfo($"[VShare] Auto-accepting catalog-listed shared mod offer for {offer.FileNameString}.");
        TryAcceptTransferOffer(offer.Id);
    }

    static bool ShouldAutoAcceptSharedModOffer(string fileName, DateTime utcNow)
    {
        lock (pendingOfferLock)
        {
            return ShouldAutoAcceptSharedModOffer(
                fileName,
                utcNow,
                _sharedModAutoAcceptUntilUtc,
                _sharedModAutoAcceptFileNames);
        }
    }

    static bool ShouldAutoAcceptSharedModOffer(
        string fileName,
        DateTime utcNow,
        DateTime autoAcceptUntilUtc,
        IReadOnlyCollection<string> catalogFileNames)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            catalogFileNames is null ||
            catalogFileNames.Count == 0 ||
            utcNow > autoAcceptUntilUtc)
        {
            return false;
        }

        return catalogFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }

    internal static bool ShouldAutoAcceptSharedModOfferForTesting(
        string fileName,
        DateTime utcNow,
        DateTime autoAcceptUntilUtc,
        IReadOnlyCollection<string> catalogFileNames)
        => ShouldAutoAcceptSharedModOffer(fileName, utcNow, autoAcceptUntilUtc, catalogFileNames);

    internal static void SetSharedModAutoAcceptCatalogForTesting(
        IEnumerable<string> fileNames,
        DateTime autoAcceptUntilUtc)
    {
        lock (pendingOfferLock)
        {
            _sharedModAutoAcceptFileNames.Clear();
            foreach (string fileName in fileNames ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    _sharedModAutoAcceptFileNames.Add(fileName);
                }
            }

            _sharedModAutoAcceptUntilUtc = autoAcceptUntilUtc;
        }
    }

    /// <summary>
    /// Normalizes server-provided preview text for use in a compact menu description.
    /// </summary>
    /// <param name="value">The candidate text.</param>
    /// <param name="fallback">The fallback text.</param>
    /// <returns>Single-line display text.</returns>
    static string SanitizePreviewText(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    readonly struct SharedModOfferCandidate(
        SharedModEntry entry,
        PluginShareMetadataStore.PluginShareMetadata metadata,
        bool hotload)
    {
        public SharedModEntry Entry { get; } = entry;
        public PluginShareMetadataStore.PluginShareMetadata Metadata { get; } = metadata;
        public bool Hotload { get; } = hotload;
    }

    readonly struct SharedModEntry(string fileName, string baseName, string metadataBaseName, string sha256, bool isZip)
    {
        public string FileName { get; } = fileName;
        public string BaseName { get; } = baseName;
        public string MetadataBaseName { get; } = metadataBaseName;
        public string Sha256 { get; } = sha256;
        public bool IsZip { get; } = isZip;
    }

    internal readonly record struct StagedReleaseIdentity(
        string Owner,
        string Repo,
        string Tag,
        string AssetName,
        string MetadataKey,
        bool FromMetadata);

    readonly struct StagedReleaseProvenance(
        StagedReleaseIdentity identity,
        string releaseDigest,
        string localSha256,
        byte[] releaseDigestBytes)
    {
        public StagedReleaseIdentity Identity { get; } = identity;
        public string ReleaseDigest { get; } = releaseDigest;
        public string LocalSha256 { get; } = localSha256;
        public byte[] ReleaseDigestBytes { get; } = releaseDigestBytes;
    }

    readonly record struct ReleaseAssetCacheKey(string Owner, string Repo, string Tag, string AssetName);

    readonly struct ReleaseAssetCacheEntry(
        GitHubReleaseClient.GitHubReleaseDigestResult result,
        DateTimeOffset expiresAt)
    {
        public GitHubReleaseClient.GitHubReleaseDigestResult Result { get; } = result;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }

    internal static bool TryResolveStagedReleaseIdentityForTesting(
        string stagedFileName,
        IReadOnlyDictionary<string, PluginShareMetadataStore.PluginShareMetadata> entries,
        out StagedReleaseIdentity identity,
        out string errorMessage)
        => TryResolveStagedReleaseIdentity(
            stagedFileName,
            (string metadataKey, out PluginShareMetadataStore.PluginShareMetadata metadata, out string lookupError) =>
            {
                if (entries.TryGetValue(metadataKey, out metadata))
                {
                    lookupError = string.Empty;
                    return true;
                }

                lookupError = $"Share metadata was not found for '{metadataKey}'.";
                return false;
            },
            out identity,
            out errorMessage);

    internal static bool TryValidateStagedReleaseDigestForTesting(
        string fileName,
        byte[] rawBytes,
        string releaseDigest,
        out string localSha256,
        out byte[] releaseDigestBytes,
        out string errorMessage)
        => TryValidateStagedReleaseDigest(
            fileName,
            rawBytes,
            releaseDigest,
            out localSha256,
            out releaseDigestBytes,
            out errorMessage);

    static bool TryResolveStagedReleaseIdentity(
        string stagedFileName,
        TryGetShareMetadataDelegate tryGetMetadata,
        out StagedReleaseIdentity identity,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(stagedFileName))
        {
            identity = default;
            errorMessage = "Staged asset file name was empty.";
            return false;
        }

        if (!IsStagedAssetFile(stagedFileName))
        {
            identity = default;
            errorMessage = $"Staged asset '{stagedFileName}' is not a supported shareable file.";
            return false;
        }

        var metadataErrors = new List<string>();
        foreach (string metadataKey in GetShareMetadataKeysForStagedFileName(stagedFileName))
        {
            if (!tryGetMetadata(metadataKey, out PluginShareMetadataStore.PluginShareMetadata metadata, out string metadataError))
            {
                metadataErrors.Add(metadataError);
                continue;
            }

            if (TryResolveStagedReleaseIdentityFromMetadata(
                    stagedFileName,
                    metadataKey,
                    metadata,
                    out identity,
                    out errorMessage))
            {
                return true;
            }

            metadataErrors.Add(errorMessage);
        }

        if (TryResolveGitHubReleaseIdentityFromStagedFileName(
                stagedFileName,
                out GitHubReleaseClient.GitHubReleaseIdentity fallbackIdentity,
                out string stagedAssetError))
        {
            identity = new StagedReleaseIdentity(
                fallbackIdentity.Owner,
                fallbackIdentity.Repo,
                fallbackIdentity.Tag,
                Path.GetFileName(stagedFileName),
                fallbackIdentity.Repo,
                false);
            errorMessage = string.Empty;
            return true;
        }

        identity = default;
        errorMessage =
            $"GitHub Release identity could not be resolved for '{stagedFileName}'. " +
            $"Metadata resolution failed: {string.Join(" ", metadataErrors)} " +
            $"Staged asset resolution failed: {stagedAssetError}";
        return false;
    }

    static bool TryResolveStagedReleaseIdentityFromMetadata(
        string stagedFileName,
        string metadataKey,
        PluginShareMetadataStore.PluginShareMetadata metadata,
        out StagedReleaseIdentity identity,
        out string errorMessage)
    {
        if (!TryParseGitHubRepo(metadata.GitHubRepo, out string owner, out string repo, out errorMessage))
        {
            identity = default;
            errorMessage = $"Share metadata for '{metadataKey}' is invalid: {errorMessage}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(metadata.GitHubTag))
        {
            identity = default;
            errorMessage = $"Share metadata for '{metadataKey}' is missing GitHubTag.";
            return false;
        }

        string assetName = string.IsNullOrWhiteSpace(metadata.GitHubAssetName)
            ? Path.GetFileName(stagedFileName)
            : metadata.GitHubAssetName.Trim();

        if (string.IsNullOrWhiteSpace(assetName))
        {
            identity = default;
            errorMessage = $"Share metadata for '{metadataKey}' did not resolve a GitHub Release asset name.";
            return false;
        }

        if (!IsStagedAssetFile(assetName))
        {
            identity = default;
            errorMessage = $"Share metadata for '{metadataKey}' references unsupported GitHubAssetName '{assetName}'.";
            return false;
        }

        identity = new StagedReleaseIdentity(
            owner,
            repo,
            metadata.GitHubTag,
            assetName,
            metadataKey,
            true);
        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Resolves the GitHub Release identity using the staged asset name in the server share folder.
    /// </summary>
    /// <param name="pluginName">The plugin name without extension.</param>
    /// <param name="identity">The resolved release identity.</param>
    /// <param name="errorMessage">An error message describing a failed resolution.</param>
    /// <returns><c>true</c> when the identity is resolved; otherwise <c>false</c>.</returns>
    static bool TryResolveGitHubReleaseIdentityFromStagedAsset(
        string pluginName,
        out GitHubReleaseClient.GitHubReleaseIdentity identity,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
        {
            identity = default;
            errorMessage = "Plugin name was empty.";
            return false;
        }

        if (!Directory.Exists(VShare.ServerModsPath))
        {
            Directory.CreateDirectory(VShare.ServerModsPath);
            VWorld.Log.LogWarning(
                $"Server share folder was missing and has been created at '{VShare.ServerModsPath}'. " +
                "Stage .dll/.zip files here before retrying.");
        }

        string stagedAssetPath = Directory.GetFiles(VShare.ServerModsPath, $"{pluginName}.*")
            .FirstOrDefault(IsStagedAssetFile);

        if (string.IsNullOrWhiteSpace(stagedAssetPath))
        {
            identity = default;
            errorMessage =
                $"No staged asset found for '{pluginName}' in '{VShare.ServerModsPath}'. " +
                $"Expected '{pluginName}.dll' or '{pluginName}.zip'.";
            return false;
        }

        return TryResolveGitHubReleaseIdentityFromStagedFileName(
            Path.GetFileName(stagedAssetPath),
            out identity,
            out errorMessage);
    }

    /// <summary>
    /// Resolves the GitHub Release identity using a staged asset file name.
    /// </summary>
    /// <param name="stagedFileName">The staged asset file name.</param>
    /// <param name="identity">The resolved release identity.</param>
    /// <param name="errorMessage">An error message describing a failed resolution.</param>
    /// <returns><c>true</c> when the identity is resolved; otherwise <c>false</c>.</returns>
    static bool TryResolveGitHubReleaseIdentityFromStagedFileName(
        string stagedFileName,
        out GitHubReleaseClient.GitHubReleaseIdentity identity,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(stagedFileName))
        {
            identity = default;
            errorMessage = "Staged asset file name was empty.";
            return false;
        }

        if (!IsStagedAssetFile(stagedFileName))
        {
            identity = default;
            errorMessage = $"Staged asset '{stagedFileName}' is not a supported shareable file.";
            return false;
        }

        string stagedBaseName = Path.GetFileNameWithoutExtension(stagedFileName);
        if (!TryParseGitHubIdentityFromFileName(stagedBaseName, out string owner, out string repo, out string tag, out errorMessage))
        {
            identity = default;
            return false;
        }

        identity = new GitHubReleaseClient.GitHubReleaseIdentity(owner, repo, tag);
        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Determines whether a staged asset file has a shareable extension.
    /// </summary>
    /// <param name="filePath">The staged asset path.</param>
    /// <returns><c>true</c> when the file extension is supported for sharing; otherwise <c>false</c>.</returns>
    static bool IsStagedAssetFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string extension = Path.GetExtension(filePath);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses the staged asset name into GitHub owner, repo, and tag values.
    /// </summary>
    /// <param name="fileBaseName">The staged asset base name without extension.</param>
    /// <param name="owner">The resolved GitHub owner.</param>
    /// <param name="repo">The resolved GitHub repository.</param>
    /// <param name="tag">The resolved GitHub tag.</param>
    /// <param name="errorMessage">An error message describing a failed parse.</param>
    /// <returns><c>true</c> when the identity is parsed; otherwise <c>false</c>.</returns>
    static bool TryParseGitHubIdentityFromFileName(
        string fileBaseName,
        out string owner,
        out string repo,
        out string tag,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(fileBaseName))
        {
            owner = string.Empty;
            repo = string.Empty;
            tag = string.Empty;
            errorMessage = "Staged asset name was empty.";
            return false;
        }

        string[] parts = fileBaseName.Contains(STAGED_ASSET_DOUBLE_DELIMITER, StringComparison.Ordinal)
            ? fileBaseName.Split(STAGED_ASSET_DOUBLE_DELIMITER, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : fileBaseName.Split(STAGED_ASSET_SINGLE_DELIMITER, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 3)
        {
            owner = string.Empty;
            repo = string.Empty;
            tag = string.Empty;
            errorMessage =
                $"Unable to derive GitHub owner/repo/tag from staged asset name '{fileBaseName}'. " +
                "Use the format 'Owner_Repo_Tag.dll' (or 'Owner__Repo__Tag.dll' when segments include underscores).";
            return false;
        }

        owner = parts[0];
        repo = parts[1];
        tag = string.Join(STAGED_ASSET_SINGLE_DELIMITER, parts.Skip(2));

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(tag))
        {
            owner = string.Empty;
            repo = string.Empty;
            tag = string.Empty;
            errorMessage =
                $"Staged asset name '{fileBaseName}' must include non-empty owner, repo, and tag segments.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Parses a GitHub repository string into owner and repo segments.
    /// </summary>
    /// <param name="githubRepo">The GitHub repository string in <c>owner/repo</c> format.</param>
    /// <param name="owner">The parsed owner segment.</param>
    /// <param name="repo">The parsed repository segment.</param>
    /// <param name="errorMessage">An error message describing a failed parse.</param>
    /// <returns><c>true</c> when the repo string is parsed successfully; otherwise <c>false</c>.</returns>
    static bool TryParseGitHubRepo(string githubRepo, out string owner, out string repo, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(githubRepo))
        {
            owner = string.Empty;
            repo = string.Empty;
            errorMessage = "Share metadata is missing GitHubRepo.";
            return false;
        }

        string[] parts = githubRepo.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            owner = string.Empty;
            repo = string.Empty;
            errorMessage = $"GitHubRepo '{githubRepo}' must be in owner/repo format.";
            return false;
        }

        owner = parts[0];
        repo = parts[1];
        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Determines whether a transfer offer can be made based on GitHub Release asset digest resolution.
    /// </summary>
    /// <param name="fileName">The staged asset file name.</param>
    /// <param name="errorMessage">An error message describing a failed resolution.</param>
    /// <returns><c>true</c> when the digest is resolved; otherwise <c>false</c>.</returns>
    static bool TryResolveShareDigestForOffer(string fileName, out string errorMessage)
    {
        if (!TryGetServerStagedFilePath(fileName, out string stagedFilePath, out errorMessage))
        {
            return false;
        }

        byte[] rawBytes;
        try
        {
            rawBytes = File.ReadAllBytes(stagedFilePath);
        }
        catch (Exception ex) when (IsStagedModFileAccessException(ex))
        {
            errorMessage = $"Unable to read staged asset '{fileName}' for provenance verification: {ex.Message}";
            return false;
        }

        return TryResolveStagedReleaseProvenance(
            fileName,
            rawBytes,
            out _,
            out errorMessage);
    }

    static bool TryGetServerStagedFilePath(string fileName, out string stagedFilePath, out string errorMessage)
    {
        stagedFilePath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            errorMessage = "File name was empty.";
            return false;
        }

        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            errorMessage = $"File name '{fileName}' must not contain a directory path.";
            return false;
        }

        if (!Directory.Exists(VShare.ServerModsPath))
        {
            Directory.CreateDirectory(VShare.ServerModsPath);
            errorMessage =
                $"Server share folder was missing and has been created at '{VShare.ServerModsPath}'. " +
                "Stage .dll/.zip files here before retrying.";
            return false;
        }

        string candidatePath = Path.Combine(VShare.ServerModsPath, fileName);
        if (!File.Exists(candidatePath) || !IsStagedAssetFile(candidatePath))
        {
            errorMessage = $"No supported staged asset named '{fileName}' was found in '{VShare.ServerModsPath}'.";
            return false;
        }

        stagedFilePath = candidatePath;
        errorMessage = string.Empty;
        return true;
    }

    static bool TryResolveStagedReleaseProvenance(
        string stagedFileName,
        byte[] rawBytes,
        out StagedReleaseProvenance provenance,
        out string errorMessage)
    {
        if (!TryResolveStagedReleaseIdentity(
                stagedFileName,
                _shareMetadataStore.TryGetPluginMetadata,
                out StagedReleaseIdentity identity,
                out string identityError))
        {
            provenance = default;
            errorMessage = identityError;
            return false;
        }

        var gitHubIdentity = new GitHubReleaseClient.GitHubReleaseIdentity(
            identity.Owner,
            identity.Repo,
            identity.Tag);

        if (!TryResolveReleaseAssetDigest(gitHubIdentity, identity.AssetName, out string digest, out string digestError))
        {
            provenance = default;
            errorMessage = $"GitHub Release asset digest lookup failed for '{identity.AssetName}': {digestError}";
            return false;
        }

        if (!TryValidateStagedReleaseDigest(
                stagedFileName,
                rawBytes,
                digest,
                out string localSha256,
                out byte[] releaseDigestBytes,
                out string validationError))
        {
            provenance = default;
            errorMessage = validationError;
            return false;
        }

        provenance = new StagedReleaseProvenance(identity, digest, localSha256, releaseDigestBytes);
        errorMessage = string.Empty;
        return true;
    }

    static bool TryValidateStagedReleaseDigest(
        string fileName,
        byte[] rawBytes,
        string releaseDigest,
        out string localSha256,
        out byte[] releaseDigestBytes,
        out string errorMessage)
    {
        localSha256 = string.Empty;
        releaseDigestBytes = [];

        if (rawBytes is null)
        {
            errorMessage = $"GitHub Release asset digest could not be computed for {fileName}: staged bytes were missing.";
            return false;
        }

        if (!TryConvertHexStringToBytes(releaseDigest, out releaseDigestBytes))
        {
            releaseDigestBytes = [];
            errorMessage = $"GitHub Release asset digest for {fileName} was not a valid SHA-256 hex string.";
            return false;
        }

        localSha256 = ComputeSha256Hex(rawBytes);
        if (!string.Equals(localSha256, releaseDigest, StringComparison.OrdinalIgnoreCase))
        {
            releaseDigestBytes = [];
            errorMessage =
                $"GitHub Release asset digest mismatch for {fileName}. " +
                $"Expected {releaseDigest}, but local file hash is {localSha256}.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Verifies that the local raw file bytes match the GitHub Release asset digest before transfer.
    /// </summary>
    /// <param name="target">The user receiving the transfer.</param>
    /// <param name="fileName">The file name being transferred.</param>
    /// <param name="pluginName">The plugin name without extension.</param>
    /// <param name="rawBytes">The raw, uncompressed bytes used for hash comparison.</param>
    /// <param name="onComplete">Callback invoked with the verification result.</param>
    /// <returns>An enumerator for coroutine execution.</returns>
    static IEnumerator VerifyShareDigestRoutine(
        User target,
        string fileName,
        string pluginName,
        byte[] rawBytes,
        Action<ReleaseDigestVerificationResult> onComplete)
    {
        bool isZip = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (!TryResolveStagedReleaseIdentity(
                fileName,
                _shareMetadataStore.TryGetPluginMetadata,
                out StagedReleaseIdentity releaseIdentity,
                out string resolveError))
        {
            string message = $"GitHub Release asset digest metadata not resolved for {pluginName}: {resolveError}";
            VWorld.Log.LogWarning(message);
            SendTransferFailureMessage(
                target,
                $"Transfer aborted: {message} Please ensure the staged asset name or ShareMetadata.json entry is valid.");
            onComplete(new ReleaseDigestVerificationResult(true, string.Empty, string.Empty, []));
            yield break;
        }

        GitHubReleaseClient.GitHubReleaseDigestResult digestResult = default;
        bool lookupCompleted = false;
        object lookupLock = new();

        _gitHubReleaseClient.BeginReleaseAssetDigestLookup(
            new GitHubReleaseClient.GitHubReleaseIdentity(
                releaseIdentity.Owner,
                releaseIdentity.Repo,
                releaseIdentity.Tag),
            releaseIdentity.AssetName,
            CancellationToken.None,
            result =>
            {
                lock (lookupLock)
                {
                    digestResult = result;
                    lookupCompleted = true;
                }
            });

        while (true)
        {
            lock (lookupLock)
            {
                if (lookupCompleted)
                {
                    break;
                }
            }

            yield return null;
        }

        if (!digestResult.IsSuccess || string.IsNullOrWhiteSpace(digestResult.Digest))
        {
            string message = $"GitHub Release asset digest lookup did not return a digest for {pluginName}: {digestResult.ErrorMessage}";
            VWorld.Log.LogWarning(message);
            SendTransferFailureMessage(target, $"Transfer aborted: {message} Please retry later.");
            onComplete(new ReleaseDigestVerificationResult(true, string.Empty, digestResult.Tag, []));
            yield break;
        }

        if (!TryValidateStagedReleaseDigest(
                fileName,
                rawBytes,
                digestResult.Digest,
                out _,
                out byte[] hashBytes,
                out string validationError))
        {
            VWorld.Log.LogWarning(validationError);
            bool invalidated = false;
            if (validationError.Contains("digest mismatch", StringComparison.OrdinalIgnoreCase) &&
                !VShare.TryInvalidateCachedEntry(fileName, pluginName, isZip, true, out string invalidateError))
            {
                VWorld.Log.LogError($"Cache invalidation failed for {fileName}: {invalidateError}");
            }
            else if (validationError.Contains("digest mismatch", StringComparison.OrdinalIgnoreCase))
            {
                invalidated = true;
            }

            string retryMessage = invalidated
                ? " Cache entry was invalidated. Please re-download the release asset from GitHub and try again."
                : " Please retry later.";
            SendTransferFailureMessage(
                target,
                $"{validationError}{retryMessage}");
            onComplete(new ReleaseDigestVerificationResult(true, digestResult.Digest, digestResult.Tag, []));
            yield break;
        }

        onComplete(new ReleaseDigestVerificationResult(false, digestResult.Digest, digestResult.Tag, hashBytes));
    }

    /// <summary>
    /// Computes the SHA-256 hex string for the provided data.
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>An uppercase hexadecimal SHA-256 string.</returns>
    static string ComputeSha256Hex(byte[] data)
    {
        using SHA256 sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data));
    }

    /// <summary>
    /// Attempts to parse a hex-encoded SHA-256 hash into raw bytes.
    /// </summary>
    /// <param name="hexString">The hex-encoded hash.</param>
    /// <param name="bytes">The parsed bytes when successful.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise <c>false</c>.</returns>
    static bool TryConvertHexStringToBytes(string hexString, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromHexString(hexString);
            return bytes.Length == Const.STANDARD_LENGTH;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
        catch (ArgumentException)
        {
            bytes = [];
            return false;
        }
    }

    /// <summary>
    /// Sends a system chat message to the target user describing a transfer failure.
    /// </summary>
    /// <param name="target">The target user.</param>
    /// <param name="message">The message content.</param>
    static void SendTransferFailureMessage(User target, string message)
    {
        FixedString512Bytes fixedMessage = new(message);
        ServerChatUtils.SendSystemMessageToClient(VWorld.EntityManager, target, ref fixedMessage);
    }

    /// <summary>
    /// Captures the installed client mod list and hashes for share comparison.
    /// </summary>
    /// <returns>The collection of client mod signatures.</returns>
    static ClientModSignature[] BuildClientModSignatures()
    {
        if (!Directory.Exists(Paths.PluginPath))
        {
            return [];
        }

        string[] dllPaths = Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories);
        var signatures = new List<ClientModSignature>(dllPaths.Length);

        foreach (string dllPath in dllPaths)
        {
            string fileName = Path.GetFileName(dllPath);
            string hash = string.Empty;

            if (TryGetFileSha256(dllPath, out byte[] hashBytes))
            {
                hash = Convert.ToHexString(hashBytes);
            }

            signatures.Add(new ClientModSignature(fileName, hash));
        }

        return signatures.ToArray();
    }

    /// <summary>
    /// Builds lookup sets for client mod file names and hashes.
    /// </summary>
    /// <param name="clientMods">The client mod signatures.</param>
    /// <returns>The lookup data for fast comparisons.</returns>
    static ClientModLookup BuildClientModLookup(ClientModSignature[] clientMods)
    {
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (clientMods is null)
        {
            return new ClientModLookup(fileNames, hashes);
        }

        foreach (ClientModSignature entry in clientMods)
        {
            if (!string.IsNullOrWhiteSpace(entry.FileName))
            {
                fileNames.Add(Path.GetFileName(entry.FileName));
            }

            if (!string.IsNullOrWhiteSpace(entry.Sha256))
            {
                hashes.Add(entry.Sha256);
            }
        }

        return new ClientModLookup(fileNames, hashes);
    }

    /// <summary>
    /// Enumerates server-side shareable mods and gathers metadata for comparison.
    /// </summary>
    /// <returns>The shareable mod entries staged on the server.</returns>
    static IReadOnlyList<SharedModEntry> GetServerShareEntries()
    {
        if (!Directory.Exists(VShare.ServerModsPath))
        {
            Directory.CreateDirectory(VShare.ServerModsPath);
            VWorld.Log.LogWarning(
                $"Server share folder was missing and has been created at '{VShare.ServerModsPath}'. " +
                "Stage .dll/.zip files here before retrying.");
            return Array.Empty<SharedModEntry>();
        }

        string[] modFiles = Directory.GetFiles(VShare.ServerModsPath);
        var entries = new List<SharedModEntry>(modFiles.Length);

        foreach (string modFile in modFiles)
        {
            if (!IsStagedAssetFile(modFile))
            {
                continue;
            }

            string fileName = Path.GetFileName(modFile);
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            bool isZip = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            byte[] rawBytes;
            try
            {
                rawBytes = File.ReadAllBytes(modFile);
            }
            catch (Exception ex) when (IsStagedModFileAccessException(ex))
            {
                VWorld.Log.LogWarning($"Skipping shared mod {fileName}: unable to read staged bytes for provenance verification: {ex.Message}");
                continue;
            }

            if (!TryResolveStagedReleaseProvenance(
                    fileName,
                    rawBytes,
                    out StagedReleaseProvenance provenance,
                    out string provenanceError))
            {
                VWorld.Log.LogWarning($"Skipping shared mod {fileName}: {provenanceError}");
                continue;
            }

            entries.Add(new SharedModEntry(
                fileName,
                baseName,
                provenance.Identity.MetadataKey,
                provenance.LocalSha256,
                isZip));
        }

        return entries;
    }

    internal static bool IsStagedModFileAccessExceptionForTesting(Exception ex)
        => IsStagedModFileAccessException(ex);

    static bool IsStagedModFileAccessException(Exception ex)
        => ex is IOException or UnauthorizedAccessException or SecurityException;

    /// <summary>
    /// Resolves a GitHub Release asset digest, caching results to reduce API calls.
    /// </summary>
    /// <param name="identity">The GitHub Release identity.</param>
    /// <param name="assetFileName">The asset file name.</param>
    /// <param name="digest">The resolved digest value.</param>
    /// <param name="errorMessage">An error message describing a failed resolution.</param>
    /// <returns><c>true</c> when the digest is resolved; otherwise <c>false</c>.</returns>
    static bool TryResolveReleaseAssetDigest(
        GitHubReleaseClient.GitHubReleaseIdentity identity,
        string assetFileName,
        out string digest,
        out string errorMessage)
    {
        GitHubReleaseClient.GitHubReleaseDigestResult result = GetReleaseAssetDigestFromCache(identity, assetFileName);

        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Digest))
        {
            digest = string.Empty;
            errorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "GitHub Release asset digest lookup did not return a digest."
                : result.ErrorMessage;
            return false;
        }

        digest = result.Digest;
        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Retrieves a GitHub Release asset digest, leveraging a local cache to avoid redundant API calls.
    /// </summary>
    /// <param name="identity">The GitHub Release identity.</param>
    /// <param name="assetFileName">The asset file name.</param>
    /// <returns>The cached or freshly retrieved digest lookup result.</returns>
    static GitHubReleaseClient.GitHubReleaseDigestResult GetReleaseAssetDigestFromCache(
        GitHubReleaseClient.GitHubReleaseIdentity identity,
        string assetFileName)
    {
        if (TryGetCachedReleaseAssetDigest(identity, assetFileName, out GitHubReleaseClient.GitHubReleaseDigestResult cachedResult))
        {
            return cachedResult;
        }

        GitHubReleaseClient.GitHubReleaseDigestResult result = _gitHubReleaseClient.GetReleaseAssetDigest(
            identity,
            assetFileName,
            CancellationToken.None);

        CacheReleaseAssetDigest(identity, assetFileName, result);
        return result;
    }

    static bool TryResolveLocalShareDigest(
        string pluginName,
        out string digest,
        out byte[] hashBytes,
        out bool configured,
        out string errorMessage)
        => TryResolveLocalShareDigest(
            [pluginName],
            _shareMetadataStore.TryGetPluginMetadata,
            out digest,
            out hashBytes,
            out configured,
            out errorMessage);

    static bool TryResolveLocalShareDigest(
        IReadOnlyList<string> metadataKeys,
        out string digest,
        out byte[] hashBytes,
        out bool configured,
        out string errorMessage)
        => TryResolveLocalShareDigest(
            metadataKeys,
            _shareMetadataStore.TryGetPluginMetadata,
            out digest,
            out hashBytes,
            out configured,
            out errorMessage);

    static bool TryResolveLocalShareDigest(
        IReadOnlyList<string> metadataKeys,
        TryGetShareMetadataDelegate tryGetMetadata,
        out string digest,
        out byte[] hashBytes,
        out bool configured,
        out string errorMessage)
    {
        digest = string.Empty;
        hashBytes = [];
        configured = false;

        foreach (string metadataKey in metadataKeys)
        {
            if (!tryGetMetadata(metadataKey, out PluginShareMetadataStore.PluginShareMetadata metadata, out _))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(metadata.LocalSha256))
            {
                continue;
            }

            configured = true;
            return TryNormalizeLocalShareDigest(metadata.LocalSha256, out digest, out hashBytes, out errorMessage);
        }

        errorMessage = string.Empty;
        return false;
    }

    static bool TryNormalizeLocalShareDigest(
        string value,
        out string digest,
        out byte[] hashBytes,
        out string errorMessage)
    {
        hashBytes = [];
        if (!GitHubReleaseClient.TryNormalizeSha256Digest(value, out digest, out errorMessage))
        {
            errorMessage = $"Share metadata LocalSha256 is invalid: {errorMessage}";
            return false;
        }

        if (!TryConvertHexStringToBytes(digest, out hashBytes))
        {
            errorMessage = "Share metadata LocalSha256 was not a valid SHA-256 hex string.";
            digest = string.Empty;
            return false;
        }

        digest = digest.ToUpperInvariant();
        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Attempts to read a cached digest lookup result for a GitHub Release asset.
    /// </summary>
    /// <param name="identity">The GitHub Release identity.</param>
    /// <param name="assetFileName">The asset file name.</param>
    /// <param name="result">The cached digest result.</param>
    /// <returns><c>true</c> when a valid cache entry is present; otherwise <c>false</c>.</returns>
    static bool TryGetCachedReleaseAssetDigest(
        GitHubReleaseClient.GitHubReleaseIdentity identity,
        string assetFileName,
        out GitHubReleaseClient.GitHubReleaseDigestResult result)
    {
        ReleaseAssetCacheKey key = CreateReleaseAssetCacheKey(identity, assetFileName);
        lock (releaseAssetDigestCacheLock)
        {
            if (releaseAssetDigestCache.TryGetValue(key, out ReleaseAssetCacheEntry entry) &&
                DateTimeOffset.UtcNow <= entry.ExpiresAt)
            {
                result = entry.Result;
                return true;
            }
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Stores a digest lookup result in the local cache.
    /// </summary>
    /// <param name="identity">The GitHub Release identity.</param>
    /// <param name="assetFileName">The asset file name.</param>
    /// <param name="result">The digest lookup result.</param>
    static void CacheReleaseAssetDigest(
        GitHubReleaseClient.GitHubReleaseIdentity identity,
        string assetFileName,
        GitHubReleaseClient.GitHubReleaseDigestResult result)
    {
        ReleaseAssetCacheKey key = CreateReleaseAssetCacheKey(identity, assetFileName);
        var entry = new ReleaseAssetCacheEntry(
            result,
            DateTimeOffset.UtcNow.AddMinutes(RELEASE_DIGEST_CACHE_TTL_MINUTES));
        lock (releaseAssetDigestCacheLock)
        {
            releaseAssetDigestCache[key] = entry;
        }
    }

    /// <summary>
    /// Creates a normalized cache key for a GitHub Release asset lookup.
    /// </summary>
    /// <param name="identity">The GitHub Release identity.</param>
    /// <param name="assetFileName">The asset file name.</param>
    /// <returns>The normalized cache key.</returns>
    static ReleaseAssetCacheKey CreateReleaseAssetCacheKey(
        GitHubReleaseClient.GitHubReleaseIdentity identity,
        string assetFileName)
    {
        return new ReleaseAssetCacheKey(
            NormalizeCacheSegment(identity.Owner),
            NormalizeCacheSegment(identity.Repo),
            NormalizeCacheSegment(identity.Tag),
            NormalizeCacheSegment(assetFileName));
    }

    /// <summary>
    /// Normalizes cache segments to ensure case-insensitive comparisons.
    /// </summary>
    /// <param name="value">The segment to normalize.</param>
    /// <returns>The normalized segment value.</returns>
    static string NormalizeCacheSegment(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Determines whether a shared mod should be offered to the client.
    /// </summary>
    /// <param name="entry">The shared mod metadata.</param>
    /// <param name="clientMods">The client mod lookup sets.</param>
    /// <returns><c>true</c> when the mod is missing on the client.</returns>
    static bool IsShareRequired(SharedModEntry entry, ClientModLookup clientMods)
    {
        string expectedDllName = entry.IsZip ? $"{entry.BaseName}.dll" : entry.FileName;

        if (clientMods.FileNames.Contains(expectedDllName))
        {
            return false;
        }

        if (!entry.IsZip && !string.IsNullOrWhiteSpace(entry.Sha256))
        {
            return !clientMods.Hashes.Contains(entry.Sha256);
        }

        return true;
    }

    /// <summary>
    /// Validates the file name length against the fixed packet length.
    /// </summary>
    /// <param name="fileName">The file name to validate.</param>
    /// <returns><c>true</c> when the file name fits the packet constraints.</returns>
    static bool IsFileNameWithinLimit(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return System.Text.Encoding.UTF8.GetByteCount(fileName) <= Const.STANDARD_LENGTH;
    }

    /// <summary>
    /// Resolves client-share metadata for a staged shared mod.
    /// </summary>
    /// <param name="entry">The shared mod entry.</param>
    /// <param name="metadata">The resolved share metadata.</param>
    /// <param name="skipReason">The reason a mod was rejected for sharing.</param>
    /// <returns><c>true</c> when the mod is client-safe; otherwise <c>false</c>.</returns>
    static bool TryGetClientShareMetadata(
        SharedModEntry entry,
        out PluginShareMetadataStore.PluginShareMetadata metadata,
        out string skipReason)
        => TrySelectClientShareMetadata(
            GetShareMetadataKeys(entry),
            _shareMetadataStore.TryGetPluginMetadata,
            out metadata,
            out skipReason);

    static bool TrySelectClientShareMetadata(
        IReadOnlyList<string> metadataKeys,
        TryGetShareMetadataDelegate tryGetMetadata,
        out PluginShareMetadataStore.PluginShareMetadata metadata,
        out string skipReason)
    {
        var errors = new List<string>();
        var unsafeKeys = new List<string>();
        foreach (string metadataKey in metadataKeys)
        {
            if (tryGetMetadata(metadataKey, out metadata, out string errorMessage))
            {
                if (IsClientShareAllowed(metadata))
                {
                    skipReason = string.Empty;
                    return true;
                }

                unsafeKeys.Add(metadataKey);
                continue;
            }

            errors.Add(errorMessage);
        }

        metadata = default;
        if (unsafeKeys.Count > 0)
        {
            skipReason =
                "Missing required client tag or ClientSafe flag in share metadata for " +
                string.Join(", ", unsafeKeys) + ".";
        }
        else
        {
            skipReason = string.Join(" ", errors);
        }

        return false;
    }

    /// <summary>
    /// Gets metadata lookup keys for a shared mod entry.
    /// </summary>
    /// <param name="entry">The shared mod entry.</param>
    /// <returns>The ordered metadata keys to try.</returns>
    static IReadOnlyList<string> GetShareMetadataKeys(SharedModEntry entry)
        => GetShareMetadataKeys(entry.BaseName, entry.MetadataBaseName);

    /// <summary>
    /// Gets metadata lookup keys for a staged asset file.
    /// </summary>
    /// <param name="stagedFileName">The staged asset file name.</param>
    /// <returns>The ordered metadata keys to try.</returns>
    internal static IReadOnlyList<string> GetShareMetadataKeysForTesting(string stagedFileName)
        => GetShareMetadataKeysForStagedFileName(stagedFileName);

    static IReadOnlyList<string> GetShareMetadataKeysForStagedFileName(string stagedFileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(stagedFileName);
        return GetShareMetadataKeys(baseName, GetShareMetadataBaseNameFromStagedFileName(stagedFileName));
    }

    static string GetShareMetadataBaseNameFromStagedFileName(string stagedFileName)
        => TryResolveGitHubReleaseIdentityFromStagedFileName(
            stagedFileName,
            out GitHubReleaseClient.GitHubReleaseIdentity identity,
            out _)
            ? identity.Repo
            : string.Empty;

    internal static bool TrySelectClientShareMetadataForTesting(
        IReadOnlyList<string> metadataKeys,
        IReadOnlyDictionary<string, PluginShareMetadataStore.PluginShareMetadata> entries,
        out PluginShareMetadataStore.PluginShareMetadata metadata,
        out string skipReason)
        => TrySelectClientShareMetadata(
            metadataKeys,
            (string metadataKey, out PluginShareMetadataStore.PluginShareMetadata entry, out string errorMessage) =>
            {
                if (entries.TryGetValue(metadataKey, out entry))
                {
                    errorMessage = string.Empty;
                    return true;
                }

                errorMessage = $"Share metadata was not found for '{metadataKey}'.";
                return false;
            },
            out metadata,
            out skipReason);

    /// <summary>
    /// Gets ordered metadata lookup keys from staged and human-legible names.
    /// </summary>
    /// <param name="baseName">The staged asset base name.</param>
    /// <param name="metadataBaseName">The preferred metadata base name.</param>
    /// <returns>The ordered metadata keys to try.</returns>
    static IReadOnlyList<string> GetShareMetadataKeys(string baseName, string metadataBaseName)
    {
        var keys = new List<string>(capacity: 2);
        AddMetadataKey(keys, baseName);
        AddMetadataKey(keys, metadataBaseName);
        return keys;
    }

    /// <summary>
    /// Adds a unique metadata key to a lookup list.
    /// </summary>
    /// <param name="keys">The key list to update.</param>
    /// <param name="key">The key candidate.</param>
    static void AddMetadataKey(List<string> keys, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            keys.Add(key);
        }
    }

    /// <summary>
    /// Determines whether share metadata allows offering a mod to clients.
    /// </summary>
    /// <param name="metadata">The share metadata to evaluate.</param>
    /// <returns><c>true</c> when the metadata marks the mod as client-safe.</returns>
    static bool IsClientShareAllowed(PluginShareMetadataStore.PluginShareMetadata metadata)
        => metadata.ClientSafe ||
           ContainsClientTag(metadata.Tags) ||
           ContainsClientTag(metadata.Categories);

    /// <summary>
    /// Determines whether a shared mod should be hotloaded after client transfer.
    /// </summary>
    /// <param name="isZip">Whether the shared mod is a ZIP archive.</param>
    /// <param name="metadata">The share metadata to evaluate.</param>
    /// <returns><c>true</c> when the DLL is client-shareable and explicitly hotload-enabled.</returns>
    static bool IsSharedModHotloadAllowed(bool isZip, PluginShareMetadataStore.PluginShareMetadata metadata)
        => !isZip && metadata.HotloadAllowed && IsClientShareAllowed(metadata);

    static void RecordSharedModRequestConsent(DateTime requestedAtUtc)
        => RecordSharedModRequestConsent(requestedAtUtc, API.Shared.VNetwork.ClientSessionGeneration);

    static void RecordSharedModRequestConsent(DateTime requestedAtUtc, long clientSessionGeneration)
    {
        lock (pendingOfferLock)
        {
            sharedModRequestConsentExpiresAtUtc = requestedAtUtc.Add(offerTimeout);
            sharedModRequestConsentClientSessionGeneration = clientSessionGeneration;
        }
    }

    static bool ShouldAutoAcceptSharedModOffer(TransferOffer offer, DateTime nowUtc)
        => ShouldAutoAcceptSharedModOffer(offer, nowUtc, API.Shared.VNetwork.ClientSessionGeneration);

    static bool ShouldAutoAcceptSharedModOffer(TransferOffer offer, DateTime nowUtc, long clientSessionGeneration)
    {
        lock (pendingOfferLock)
        {
            return offer.Clientbound
                && clientSessionGeneration == sharedModRequestConsentClientSessionGeneration
                && nowUtc <= sharedModRequestConsentExpiresAtUtc
                && ShouldAutoAcceptSharedModOffer(
                    offer.FileNameString,
                    nowUtc,
                    _sharedModAutoAcceptUntilUtc,
                    _sharedModAutoAcceptFileNames);
        }
    }

    internal static void RecordSharedModRequestConsentForTesting(DateTime requestedAtUtc, long clientSessionGeneration)
        => RecordSharedModRequestConsent(requestedAtUtc, clientSessionGeneration);

    internal static void ClearSharedModRequestConsentForTesting()
        => ClearSharedModRequestConsent(refreshMenuDescription: false);

    static void ClearSharedModRequestConsent()
        => ClearSharedModRequestConsent(refreshMenuDescription: true);

    static void ClearSharedModRequestConsent(bool refreshMenuDescription)
    {
        lock (pendingOfferLock)
        {
            sharedModRequestConsentExpiresAtUtc = DateTime.MinValue;
            sharedModRequestConsentClientSessionGeneration = -1;
            _sharedModAutoAcceptUntilUtc = DateTime.MinValue;
            _sharedModAutoAcceptFileNames.Clear();
            _latestSharedClientModPreview = null;
        }

        if (refreshMenuDescription && VWorld.IsClient)
        {
            OptionsManager.RefreshButtonDescription(REQUEST_SHARED_MODS_BUTTON_ID);
        }
    }

    internal static bool ShouldAutoAcceptSharedModOfferForTesting(TransferOffer offer, DateTime nowUtc, long clientSessionGeneration)
        => ShouldAutoAcceptSharedModOffer(offer, nowUtc, clientSessionGeneration);

    /// <summary>
    /// Exposes shared-mod hotload eligibility for focused unit coverage.
    /// </summary>
    /// <param name="isZip">Whether the shared mod is a ZIP archive.</param>
    /// <param name="metadata">The share metadata to evaluate.</param>
    /// <returns><c>true</c> when the mod should be offered for runtime hotload.</returns>
    internal static bool IsSharedModHotloadAllowedForTesting(
        bool isZip,
        PluginShareMetadataStore.PluginShareMetadata metadata)
        => IsSharedModHotloadAllowed(isZip, metadata);

    internal static bool TryNormalizeLocalShareDigestForTesting(
        string value,
        out string digest,
        out byte[] hashBytes,
        out string errorMessage)
        => TryNormalizeLocalShareDigest(value, out digest, out hashBytes, out errorMessage);

    internal static bool TryResolveLocalShareDigestForTesting(
        IReadOnlyList<string> metadataKeys,
        IReadOnlyDictionary<string, PluginShareMetadataStore.PluginShareMetadata> entries,
        out string digest,
        out byte[] hashBytes,
        out bool configured,
        out string errorMessage)
        => TryResolveLocalShareDigest(
            metadataKeys,
            (string metadataKey, out PluginShareMetadataStore.PluginShareMetadata entry, out string metadataError) =>
            {
                if (entries.TryGetValue(metadataKey, out entry))
                {
                    metadataError = string.Empty;
                    return true;
                }

                metadataError = $"Share metadata was not found for '{metadataKey}'.";
                return false;
            },
            out digest,
            out hashBytes,
            out configured,
            out errorMessage);

    /// <summary>
    /// Checks whether a tag collection includes the client tag.
    /// </summary>
    /// <param name="tags">The tags to scan.</param>
    /// <returns><c>true</c> if the client tag is present; otherwise <c>false</c>.</returns>
    static bool ContainsClientTag(IReadOnlyList<string> tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return false;
        }

        foreach (string tag in tags)
        {
            if (string.Equals(tag, CLIENT_TAG, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /*  
    internal readonly struct FileAck
    {
        public readonly Guid Id;
        public readonly bool Ok;
        public FileAck(Guid id, bool ok)
        {
            Id = id;
            Ok = ok;
        }
    }

    static void OnFileAck(FileAck ack)
    {
        VWorld.Log.LogWarning($"File transfer {(ack.Ok ? "succeeded" : "failed")} ~ ID: {ack.Id} ({DateTime.Now.TimeOfDay})");
    }

    public static string GetDllPath(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || !filename.EndsWith(".dll"))
            return string.Empty;

        string pluginsPath = Paths.PluginPath;
        var dllPaths = Directory.GetFiles(pluginsPath, "*.dll", SearchOption.AllDirectories);

        string targetName = Path.GetFileName(filename);
        var match = dllPaths.FirstOrDefault(p =>
            string.Equals(Path.GetFileName(p), targetName, StringComparison.OrdinalIgnoreCase));

        return match ?? string.Empty;
    }
    */
}
