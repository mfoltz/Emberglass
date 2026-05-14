using BepInEx.Configuration;
using BepInEx.Logging;

namespace Emberglass.API.Shared.Config;

/// <summary>
/// Represents the gameplay-facing settings snapshot for the demo configuration.
/// </summary>
/// <remarks>
/// Experimental: demo-only API that may change or be removed.
/// </remarks>
public sealed class MySettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MySettings"/> class.
    /// </summary>
    /// <param name="maxActivePets">The maximum number of active pets allowed.</param>
    /// <param name="sprintMultiplier">The sprint multiplier applied to the player.</param>
    public MySettings(int maxActivePets, float sprintMultiplier)
    {
        MaxActivePets = maxActivePets;
        SprintMultiplier = sprintMultiplier;
    }

    /// <summary>
    /// Gets the maximum number of active pets allowed.
    /// </summary>
    public int MaxActivePets { get; }

    /// <summary>
    /// Gets the sprint multiplier applied to the player.
    /// </summary>
    public float SprintMultiplier { get; }

    /// <summary>
    /// Creates a copy of the settings with an updated maximum pet count.
    /// </summary>
    /// <param name="maxActivePets">The updated maximum pet count.</param>
    /// <returns>The updated settings snapshot.</returns>
    public MySettings WithMaxActivePets(int maxActivePets) => new(maxActivePets, SprintMultiplier);

    /// <summary>
    /// Creates a copy of the settings with an updated sprint multiplier.
    /// </summary>
    /// <param name="sprintMultiplier">The updated sprint multiplier.</param>
    /// <returns>The updated settings snapshot.</returns>
    public MySettings WithSprintMultiplier(float sprintMultiplier) => new(MaxActivePets, sprintMultiplier);
}

/// <summary>
/// Defines the config bindings for <see cref="MySettings"/>.
/// </summary>
/// <remarks>
/// Experimental: demo-only API that may change or be removed.
/// </remarks>
public sealed class MySettingsSpec : ConfigSpec<MySettings>
{
    const string SectionGameplay = "Gameplay";

    /// <summary>
    /// Initializes a new instance of the <see cref="MySettingsSpec"/> class.
    /// </summary>
    public MySettingsSpec()
    {
        RegisterBinding(new Binding<MySettings, int>(
            SectionGameplay,
            "MaxActivePets",
            3,
            "Maximum number of active pets allowed.",
            settings => settings.MaxActivePets,
            (settings, value) => settings.WithMaxActivePets(Math.Max(0, value))));

        RegisterBinding(new Binding<MySettings, float>(
            SectionGameplay,
            "SprintMultiplier",
            1.0f,
            "Sprint speed multiplier applied to the player.",
            settings => settings.SprintMultiplier,
            (settings, value) => settings.WithSprintMultiplier(MathF.Max(0.1f, value))));
    }
}

/// <summary>
/// Demonstrates how to wire live settings in plugin initialization and gameplay code.
/// </summary>
/// <remarks>
/// Experimental: demo-only API that may change or be removed.
/// </remarks>
public sealed class ConfigDemo
{
    readonly ManualLogSource logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigDemo"/> class.
    /// </summary>
    /// <param name="configFile">The configuration file to bind against.</param>
    /// <param name="mainThreadInvoker">The main-thread invoker used for reload scheduling.</param>
    /// <param name="logger">The logger used for reload diagnostics.</param>
    public ConfigDemo(ConfigFile configFile, IMainThreadInvoker mainThreadInvoker, ManualLogSource logger)
    {
        if (configFile is null)
        {
            throw new ArgumentNullException(nameof(configFile));
        }

        if (mainThreadInvoker is null)
        {
            throw new ArgumentNullException(nameof(mainThreadInvoker));
        }

        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        MySettingsSpec spec = new();
        MySettings seedSettings = new(3, 1.0f);

        Settings = new LiveSettings<MySettings>(spec, configFile, mainThreadInvoker, seedSettings, "MySettings reload requested");
        Settings.Reloaded += HandleSettingsReloaded;
    }

    /// <summary>
    /// Gets the live settings wrapper created during plugin initialization.
    /// </summary>
    public LiveSettings<MySettings> Settings { get; }

    /// <summary>
    /// Reads the current snapshot for gameplay logic without touching config entries directly.
    /// </summary>
    /// <returns>The sprint multiplier to apply for the current frame.</returns>
    public float GetSprintMultiplierForGameplay()
    {
        var snapshot = Settings.Current;
        return snapshot.SprintMultiplier;
    }

    void HandleSettingsReloaded(object sender, ReloadContext<MySettings> context)
    {
        string changedKeys = context.ChangedKeys.Count == 0
            ? "(no changes)"
            : string.Join(", ", context.ChangedKeys.OrderBy(key => key, StringComparer.Ordinal));

        logger.LogInfo($"MySettings reloaded ({context.Reason}). Changed: {changedKeys}");
    }
}
