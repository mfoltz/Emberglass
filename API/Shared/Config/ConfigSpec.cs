using BepInEx.Configuration;
using Emberglass.API.Shared;

namespace Emberglass.API.Shared.Config;

/// <summary>
/// Provides a binding registry for configuration-backed settings snapshots.
/// </summary>
/// <typeparam name="TSettings">The settings type being constructed.</typeparam>
public class ConfigSpec<TSettings>
    where TSettings : class
{
    readonly List<IBinding<TSettings>> bindings = new();
    bool isBound;

    /// <summary>
    /// Registers a binding definition that will be bound to a config file.
    /// </summary>
    /// <param name="binding">The binding to register.</param>
    public void RegisterBinding(IBinding<TSettings> binding)
    {
        if (binding is null)
        {
            throw new ArgumentNullException(nameof(binding));
        }

        if (isBound)
        {
            throw new InvalidOperationException("Bindings cannot be registered after binding has occurred.");
        }

        bindings.Add(binding);
    }

    /// <summary>
    /// Binds all registered bindings to the provided config file.
    /// </summary>
    /// <param name="configFile">The config file used for binding.</param>
    public void Bind(ConfigFile configFile)
    {
        if (configFile is null)
        {
            throw new ArgumentNullException(nameof(configFile));
        }

        if (isBound)
        {
            throw new InvalidOperationException("Bindings have already been bound.");
        }

        foreach (var binding in bindings)
        {
            binding.Bind(configFile);

            if (VWorld.IsServer && binding is IServerConfigChangeBinding serverBinding)
            {
                serverBinding.RegisterServerChangeHandler();
            }
        }

        isBound = true;
    }

    /// <summary>
    /// Builds a new settings snapshot by applying each registered binding.
    /// </summary>
    /// <param name="seed">The settings snapshot to use as the base.</param>
    /// <param name="oldSettings">The previous settings snapshot, if available.</param>
    /// <param name="changedKeys">The collection that receives any detected changes.</param>
    /// <returns>The updated settings snapshot.</returns>
    internal TSettings BuildSnapshot(TSettings seed, TSettings oldSettings, ISet<string> changedKeys)
    {
        if (seed is null)
        {
            throw new ArgumentNullException(nameof(seed));
        }

        if (changedKeys is null)
        {
            throw new ArgumentNullException(nameof(changedKeys));
        }

        var settings = seed;

        foreach (var binding in bindings)
        {
            settings = binding.Apply(settings, oldSettings, changedKeys);
        }

        return settings;
    }
}
