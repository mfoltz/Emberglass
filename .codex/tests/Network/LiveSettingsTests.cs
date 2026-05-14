using BepInEx.Configuration;
using Emberglass.API.Shared;
using Emberglass.API.Shared.Config;
using Xunit;

namespace Emberglass.Tests.Network;

/// <summary>
/// Covers live settings reload scheduling behavior.
/// </summary>
[Collection("Assembly setup")]
public sealed class LiveSettingsTests : IDisposable
{
    readonly string configFilePath;

    /// <summary>
    /// Initializes an isolated config path for the test.
    /// </summary>
    public LiveSettingsTests()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), "EmberglassTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        configFilePath = Path.Combine(directoryPath, "settings.cfg");
    }

    /// <summary>
    /// Ensures a reload requested while a queued reload is being processed is not dropped.
    /// </summary>
    [Fact]
    public void RequestReload_PreservesReloadRequestedDuringInFlightReload()
    {
        var invoker = new QueueingMainThreadInvoker();
        var settings = new ReloadSettings
        {
            SecondReloadRequested = true
        };
        var spec = new ConfigSpec<ReloadSettings>();

        LiveSettings<ReloadSettings> liveSettings = null!;
        spec.RegisterBinding(new Binding<ReloadSettings, bool>(
            "General",
            "Enabled",
            false,
            "Test toggle.",
            current => current.Enabled,
            (current, value) =>
            {
                current.Enabled = value;
                if (!current.SecondReloadRequested)
                {
                    current.SecondReloadRequested = true;
                    liveSettings.RequestReload("second");
                }

                return current;
            }));

        liveSettings = new LiveSettings<ReloadSettings>(
            spec,
            new ConfigFile(configFilePath, true),
            invoker,
            settings,
            "fallback");
        settings.SecondReloadRequested = false;

        List<string> reasons = new();
        liveSettings.Reloaded += (_, context) => reasons.Add(context.Reason);

        liveSettings.RequestReload("first");
        invoker.Drain();

        Assert.Equal(new[] { "first", "second" }, reasons);
        Assert.Equal(2, liveSettings.Version);
        Assert.Empty(invoker.QueuedActions);
    }

    /// <summary>
    /// Deletes the temporary config directory.
    /// </summary>
    public void Dispose()
    {
        string directoryPath = Path.GetDirectoryName(configFilePath)!;
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, true);
        }
    }

    sealed class ReloadSettings
    {
        public bool Enabled { get; set; }

        public bool SecondReloadRequested { get; set; }
    }

    sealed class QueueingMainThreadInvoker : IMainThreadInvoker
    {
        readonly Queue<Action> queuedActions = new();

        public bool IsMainThread => true;

        public IReadOnlyCollection<Action> QueuedActions => queuedActions;

        public void Run(Action action)
        {
            if (action is null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            queuedActions.Enqueue(action);
        }

        public void Drain()
        {
            while (queuedActions.TryDequeue(out var action))
            {
                action();
            }
        }
    }
}
