using Emberglass.Patches.Shared;

namespace Emberglass.Systems;
/// <summary>
/// Registers framework-owned observer systems that should be injected into ECS worlds.
/// </summary>
internal static class ObserverSystemRegistry
{
    static readonly Type[] _serverObserverSystems =
    [
        typeof(PlayerCharacterPresenceObserverSystem)
    ];

    /// <summary>
    /// Registers all currently owned observer systems with the world bootstrap injector.
    /// </summary>
    internal static void RegisterAll()
    {
        WorldBootstrapPatches.RegisterServerSystems(_serverObserverSystems);
    }
}
