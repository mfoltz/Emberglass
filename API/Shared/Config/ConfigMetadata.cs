namespace Emberglass.API.Shared.Config;

/// <summary>
/// Defines the configuration scope that owns a binding.
/// </summary>
public enum ConfigScope
{
    /// <summary>
    /// The config entry is client-only.
    /// </summary>
    Client,

    /// <summary>
    /// The config entry is server-only.
    /// </summary>
    Server,

    /// <summary>
    /// The config entry is shared across client and server.
    /// </summary>
    Shared
}

/// <summary>
/// Defines when reloads should be requested after a menu-driven change.
/// </summary>
public enum ReloadPolicy
{
    /// <summary>
    /// Never automatically request a reload.
    /// </summary>
    None,

    /// <summary>
    /// Request a reload when the value changes.
    /// </summary>
    OnChange,

    /// <summary>
    /// Require callers to trigger reloads manually.
    /// </summary>
    Manual
}
