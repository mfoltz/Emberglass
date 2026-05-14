namespace Emberglass.API.Shared.Config;

/// <summary>
/// Defines a binding that can register server-side change handlers.
/// </summary>
interface IServerConfigChangeBinding
{
    /// <summary>
    /// Gets the scope that owns the config entry.
    /// </summary>
    ConfigScope Scope { get; }

    /// <summary>
    /// Registers server-side handlers for config change requests.
    /// </summary>
    void RegisterServerChangeHandler();
}
