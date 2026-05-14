using Emberglass.API.Shared;
using Unity.Entities;
using static Emberglass.API.Server.ServerModules.PlayerPresenceModules;
using static Emberglass.API.Shared.VEvents;

namespace Emberglass.API.Server;
/// <summary>
/// Emits opt-in diagnostic lines for server-side player presence events during manual validation.
/// </summary>
internal static class PlayerPresenceValidationProbe
{
    /// <summary>
    /// Enables local diagnostic logging for player character attach and detach events.
    /// </summary>
    internal static bool IsEnabled { get; set; } = IsEnvironmentEnabled();

    static bool _initialized;

    /// <summary>
    /// Subscribes to player presence events when the probe is enabled.
    /// </summary>
    internal static void Initialize()
    {
        if (!IsEnabled || _initialized)
        {
            return;
        }

        ModuleRegistry.Subscribe<PlayerCharacterAttached>(OnPlayerCharacterAttached);
        ModuleRegistry.Subscribe<PlayerCharacterDetached>(OnPlayerCharacterDetached);
        _initialized = true;
    }

    /// <summary>
    /// Removes player presence subscriptions when the probe is active.
    /// </summary>
    internal static void Uninitialize()
    {
        if (!_initialized)
        {
            return;
        }

        ModuleRegistry.Unsubscribe<PlayerCharacterAttached>(OnPlayerCharacterAttached);
        ModuleRegistry.Unsubscribe<PlayerCharacterDetached>(OnPlayerCharacterDetached);
        _initialized = false;
    }

    static void OnPlayerCharacterAttached(PlayerCharacterAttached args)
        => LogDiagnostic("attach", args.PlayerInfo, args.CharacterEntity);

    static void OnPlayerCharacterDetached(PlayerCharacterDetached args)
        => LogDiagnostic("detach", args.PlayerInfo, args.CharacterEntity);

    static void LogDiagnostic(string eventName, Players.PlayerInfo playerInfo, Entity characterEntity)
    {
        VWorld.Log.LogInfo(
            $"[PlayerPresenceValidationProbe] event={eventName} steamId={playerInfo.SteamId} user={FormatEntity(playerInfo.UserEntity)} character={FormatEntity(characterEntity)} name=\"{GetCharacterName(playerInfo)}\"");
    }

    static string GetCharacterName(Players.PlayerInfo playerInfo)
    {
        try
        {
            return playerInfo.Name;
        }
        catch
        {
            return "<unknown>";
        }
    }

    static string FormatEntity(Entity entity)
        => entity == Entity.Null
            ? "null"
            : $"{entity.Index}:{entity.Version}";

    static bool IsEnvironmentEnabled()
    {
        string value = Environment.GetEnvironmentVariable("EMBERGLASS_PLAYER_PRESENCE_PROBE");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
