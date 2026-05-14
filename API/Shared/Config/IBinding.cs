using BepInEx.Configuration;

namespace Emberglass.API.Shared.Config;

/// <summary>
/// Defines a binding that maps config entries to settings snapshots.
/// </summary>
/// <typeparam name="TSettings">The settings type being constructed.</typeparam>
public interface IBinding<TSettings>
    where TSettings : class
{
    /// <summary>
    /// Gets the key used to report changes for this binding.
    /// </summary>
    string ChangedKey { get; }

    /// <summary>
    /// Binds the underlying config entry to the provided config file.
    /// </summary>
    /// <param name="configFile">The config file to bind against.</param>
    void Bind(ConfigFile configFile);

    /// <summary>
    /// Applies the bound value to the provided settings snapshot.
    /// </summary>
    /// <param name="settings">The settings snapshot to update.</param>
    /// <param name="oldSettings">The previous settings snapshot, if available.</param>
    /// <param name="changedKeys">The collection that receives any detected changes.</param>
    /// <returns>The updated settings snapshot.</returns>
    TSettings Apply(TSettings settings, TSettings oldSettings, ISet<string> changedKeys);
}
