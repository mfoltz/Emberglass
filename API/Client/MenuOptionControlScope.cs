namespace Emberglass.API.Client;

/// <summary>
/// Describes how a menu option is controlled.
/// </summary>
public enum MenuOptionControlScope
{
    /// <summary>
    /// No explicit control scope metadata.
    /// </summary>
    None = 0,
    /// <summary>
    /// The option is client-only and applies immediately.
    /// </summary>
    ClientOnlyLive = 1,
    /// <summary>
    /// The option is controlled by the server and requires approval.
    /// </summary>
    ServerControlled = 2
}
