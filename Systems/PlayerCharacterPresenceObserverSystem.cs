using Emberglass.API.Server;
using Emberglass.API.Shared;
using Il2CppInterop.Runtime;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using static Emberglass.API.Server.ServerModules.PlayerPresenceModules;

namespace Emberglass.Systems;
public sealed class PlayerCharacterPresenceObserverSystem : SystemBase
{
    readonly Dictionary<ulong, Entity> lastCharacterBySteamId = [];
    readonly HashSet<ulong> seenSteamIdsThisTick = [];
    readonly List<ulong> staleSteamIds = [];

    EntityQuery userQuery;
    EntityStorageInfoLookup entityStorageInfoLookup;

    public override void OnCreate()
    {
        userQuery = CreateUserQuery();
        entityStorageInfoLookup = GetEntityStorageInfoLookup();
    }

    public override void OnDestroy()
    {
        lastCharacterBySteamId.Clear();
        seenSteamIdsThisTick.Clear();
        staleSteamIds.Clear();
    }

    public override void OnUpdate()
    {
        if (!IsReady)
        {
            return;
        }

        entityStorageInfoLookup.Update(this);
        seenSteamIdsThisTick.Clear();

        NativeArray<Entity> entities = userQuery.ToEntityArray(Allocator.Temp);
        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity userEntity = entities[i];
                User user = userEntity.GetUser();

                if (user.PlatformId == 0)
                {
                    continue;
                }

                ulong steamId = user.PlatformId;
                seenSteamIdsThisTick.Add(steamId);

                Entity currentCharacterEntity = user.LocalCharacter.GetEntityOnServer();
                HandleObservedUser(userEntity, steamId, currentCharacterEntity);
            }
        }
        finally
        {
            if (entities.IsCreated)
            {
                entities.Dispose();
            }
        }

        FlushUsersMissingThisTick();
    }

    void HandleObservedUser(Entity userEntity, ulong steamId, Entity currentCharacterEntity)
    {
        bool hasCurrentCharacter = IsAttachedCharacter(currentCharacterEntity);

        if (!lastCharacterBySteamId.TryGetValue(steamId, out Entity previousCharacterEntity))
        {
            if (hasCurrentCharacter)
            {
                var playerInfo = new Players.PlayerInfo(userEntity, currentCharacterEntity);
                RaiseAttached(playerInfo, currentCharacterEntity);
                lastCharacterBySteamId[steamId] = currentCharacterEntity;
            }

            return;
        }

        if (!hasCurrentCharacter)
        {
            var playerInfo = new Players.PlayerInfo(userEntity, previousCharacterEntity);
            RaiseDetached(playerInfo, previousCharacterEntity);
            lastCharacterBySteamId.Remove(steamId);
            return;
        }

        if (previousCharacterEntity == currentCharacterEntity)
        {
            return;
        }

        RaiseDetached(new Players.PlayerInfo(userEntity, previousCharacterEntity), previousCharacterEntity);
        RaiseAttached(new Players.PlayerInfo(userEntity, currentCharacterEntity), currentCharacterEntity);
        lastCharacterBySteamId[steamId] = currentCharacterEntity;
    }

    void FlushUsersMissingThisTick()
    {
        staleSteamIds.Clear();

        foreach ((ulong steamId, _) in lastCharacterBySteamId)
        {
            if (!seenSteamIdsThisTick.Contains(steamId))
            {
                staleSteamIds.Add(steamId);
            }
        }

        foreach (ulong staleSteamId in staleSteamIds)
        {
            Entity previousCharacterEntity = lastCharacterBySteamId[staleSteamId];

            if (staleSteamId.TryGetPlayerInfo(out Players.PlayerInfo playerInfo))
            {
                playerInfo.CharacterEntity = previousCharacterEntity;
                RaiseDetached(playerInfo, previousCharacterEntity);
            }

            lastCharacterBySteamId.Remove(staleSteamId);
        }
    }

    bool IsAttachedCharacter(Entity characterEntity)
        => characterEntity != Entity.Null && entityStorageInfoLookup.Exists(characterEntity);

    EntityQuery CreateUserQuery()
    {
        EntityQueryBuilder queryBuilder = new(Allocator.Temp);
        try
        {
            queryBuilder
                .WithAllRO<User>()
                .IncludeDisabled();

            return EntityManager.CreateEntityQuery(ref queryBuilder);
        }
        finally
        {
            queryBuilder.Dispose();
        }
    }
}
