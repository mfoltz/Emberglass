using BepInEx.Configuration;

namespace Emberglass.API.Shared.Config;

/// <summary>
/// Provides live settings snapshots with main-thread reload scheduling.
/// </summary>
/// <typeparam name="TSettings">The settings type being managed.</typeparam>
public sealed class LiveSettings<TSettings>
    where TSettings : class
{
    readonly ConfigSpec<TSettings> configSpec;
    readonly IMainThreadInvoker mainThreadInvoker;
    readonly object reasonLock = new();
    readonly string fallbackReason;
    int reloadRequested;
    string pendingReason;
    TSettings current;
    long version;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveSettings{TSettings}"/> class.
    /// </summary>
    /// <param name="configSpec">The config specification used for binding and snapshots.</param>
    /// <param name="configFile">The config file to bind against.</param>
    /// <param name="mainThreadInvoker">The invoker used to schedule main-thread reloads.</param>
    /// <param name="seedSettings">The seed settings snapshot to update.</param>
    /// <param name="fallbackReason">The fallback reason used when none is provided.</param>
    public LiveSettings(
        ConfigSpec<TSettings> configSpec,
        ConfigFile configFile,
        IMainThreadInvoker mainThreadInvoker,
        TSettings seedSettings,
        string fallbackReason = "Config reload requested")
    {
        this.configSpec = configSpec ?? throw new ArgumentNullException(nameof(configSpec));
        this.mainThreadInvoker = mainThreadInvoker ?? throw new ArgumentNullException(nameof(mainThreadInvoker));
        this.fallbackReason = string.IsNullOrWhiteSpace(fallbackReason)
            ? throw new ArgumentException("Fallback reason is required.", nameof(fallbackReason))
            : fallbackReason;

        if (configFile is null)
        {
            throw new ArgumentNullException(nameof(configFile));
        }

        if (seedSettings is null)
        {
            throw new ArgumentNullException(nameof(seedSettings));
        }

        configSpec.Bind(configFile);
        current = configSpec.BuildSnapshot(seedSettings, null, new HashSet<string>());
    }

    /// <summary>
    /// Occurs when settings are reloaded.
    /// </summary>
    public event EventHandler<ReloadContext<TSettings>> Reloaded;

    /// <summary>
    /// Gets the current settings snapshot.
    /// </summary>
    public TSettings Current => Volatile.Read(ref current);

    /// <summary>
    /// Gets the current version of the settings snapshot.
    /// </summary>
    public long Version => Interlocked.Read(ref version);

    /// <summary>
    /// Requests a reload on the main thread, coalescing multiple requests.
    /// </summary>
    /// <param name="reason">The reason for the reload.</param>
    public void RequestReload(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }

        bool shouldSchedule;
        lock (reasonLock)
        {
            pendingReason = reason;
            shouldSchedule = reloadRequested == 0;
            reloadRequested = 1;
        }

        if (shouldSchedule)
        {
            mainThreadInvoker.Run(ProcessPendingReloads);
        }
    }

    /// <summary>
    /// Reloads settings immediately on the main thread.
    /// </summary>
    /// <param name="reason">The reason for the reload.</param>
    public void ReloadNow(string reason)
    {
        if (!mainThreadInvoker.IsMainThread)
        {
            throw new InvalidOperationException("ReloadNow must be invoked on the main thread.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reason is required.", nameof(reason));
        }

        var previous = Current;
        HashSet<string> changedKeys = new(StringComparer.Ordinal);
        var next = configSpec.BuildSnapshot(previous, previous, changedKeys);
        long nextVersion = Interlocked.Increment(ref version);

        Interlocked.Exchange(ref current, next);

        Reloaded?.Invoke(this, new ReloadContext<TSettings>(previous, next, reason, nextVersion, changedKeys));
    }

    void ProcessPendingReloads()
    {
        try
        {
            while (true)
            {
                ReloadNow(ConsumePendingReason());

                lock (reasonLock)
                {
                    if (string.IsNullOrWhiteSpace(pendingReason))
                    {
                        reloadRequested = 0;
                        return;
                    }
                }
            }
        }
        catch
        {
            lock (reasonLock)
            {
                reloadRequested = 0;
            }

            throw;
        }
    }

    string ConsumePendingReason()
    {
        lock (reasonLock)
        {
            string reason = string.IsNullOrWhiteSpace(pendingReason) ? fallbackReason : pendingReason;
            pendingReason = null;
            return reason;
        }
    }
}
