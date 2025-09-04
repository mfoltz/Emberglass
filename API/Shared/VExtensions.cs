using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Emberglass.API.Shared;
public static class VExtensions
{
    public delegate void ActionRefHandler<T>(ref T item);
    static EntityManager EntityManager => VWorld.EntityManager;

    const string PREFIX = "Entity(";
    const int LENGTH = 7;
    public static void SendSystemMessage(this User user, string message)
    {
        if (!VWorld.IsServer)
        {
            throw new Exception("SendSystemMessage can only be called on the server.");
        }

        FixedString512Bytes fixedMessage = new(message);
        ServerChatUtils.SendSystemMessageToClient(VWorld.Server.EntityManager, user, ref fixedMessage);
    }
    static void With<T>(this Entity entity, ActionRefHandler<T> action) where T : struct
    {
        T item = entity.Read<T>();
        action(ref item);

        EntityManager.SetComponentData(entity, item);
    }
    public static void AddWith<T>(this Entity entity, ActionRefHandler<T> action) where T : struct
    {
        if (!entity.Has<T>())
        {
            entity.Add<T>();
        }

        entity.With(action);
    }
    public static void HasWith<T>(this Entity entity, ActionRefHandler<T> action) where T : struct
    {
        if (entity.Has<T>())
        {
            entity.With(action);
        }
    }
    public static void Write<T>(this Entity entity, T componentData) where T : struct
    {
        EntityManager.SetComponentData(entity, componentData);
    }
    public static T Read<T>(this Entity entity) where T : struct
    {
        return EntityManager.GetComponentData<T>(entity);
    }
    public static Entity Create(this ComponentType[] components)
    {
        return EntityManager.CreateEntity(components);
    }
    public static void Receive(this Entity entity)
    {
        entity.Remove<ReceiveNetworkEventTag>();
        entity.Destroy();
    }
    public static DynamicBuffer<T> ReadBuffer<T>(this Entity entity) where T : struct
    {
        return EntityManager.GetBuffer<T>(entity);
    }
    public static DynamicBuffer<T> AddBuffer<T>(this Entity entity) where T : struct
    {
        return EntityManager.AddBuffer<T>(entity);
    }
    public static bool TryGetComponent<T>(this Entity entity, out T componentData) where T : struct
    {
        componentData = default;

        if (entity.Has<T>())
        {
            componentData = entity.Read<T>();
            return true;
        }

        return false;
    }
    public static bool Has<T>(this Entity entity) where T : struct
    {
        return EntityManager.HasComponent(entity, new(Il2CppType.Of<T>()));
    }
    public static void Add<T>(this Entity entity) where T : struct
    {
        if (!entity.Has<T>())
        {
            EntityManager.AddComponent(entity, new(Il2CppType.Of<T>()));
        }
    }
    public static void Remove<T>(this Entity entity) where T : struct
    {
        if (entity.Has<T>())
        {
            EntityManager.RemoveComponent(entity, new(Il2CppType.Of<T>()));
        }
    }
    public static bool IsBuff(this Entity entity)
        => entity.Has<Buff>();
    public static void Destroy(this Entity entity, bool immediate = false)
    {
        if (!entity.Exists())
        {
            return;
        }

        bool isBuff = entity.IsBuff();

        if (immediate && !isBuff)
        {
            EntityManager.DestroyEntity(entity);
        }
        else if (isBuff)
        {
            DestroyUtility.Destroy(EntityManager, entity, DestroyDebugReason.TryRemoveBuff);
        }
        else
        {
            DestroyUtility.Destroy(EntityManager, entity);
        }
    }
    public static bool Exists(this Entity entity)
    {
        return entity.HasValue()
            && entity.IndexWithinCapacity()
            && EntityManager.Exists(entity);
    }
    public static bool HasValue(this Entity entity)
    {
        return entity != Entity.Null;
    }
    public static bool IndexWithinCapacity(this Entity entity)
    {
        string entityStr = entity.ToString();
        ReadOnlySpan<char> span = entityStr.AsSpan();

        if (!span.StartsWith(PREFIX))
        {
            return false;
        }

        span = span[LENGTH..];

        int colon = span.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> tail = span[(colon + 1)..];

        int closeRel = tail.IndexOf(')');
        if (closeRel <= 0)
        {
            return false;
        }

        if (!int.TryParse(span[..colon], out int index))
        {
            return false;
        }

        if (!int.TryParse(tail[..closeRel], out _))
        {
            return false;
        }

        int capacity = EntityManager.EntityCapacity;
        return (uint)index < (uint)capacity;
    }
    public static bool IsDisabled(this Entity entity)
    {
        return entity.Has<Disabled>();
    }
    public static bool IsPlayer(this Entity entity)
    {
        return entity.Has<PlayerCharacter>();
    }
    public static bool IsVBlood(this Entity entity)
    {
        return entity.Has<VBloodUnit>() && entity.Has<VBloodConsumeSource>();
    }
    public static bool IsGateBoss(this Entity entity)
    {
        return entity.Has<VBloodUnit>() && !entity.Has<VBloodConsumeSource>();
    }
    public static bool IsVBloodOrGateBoss(this Entity entity)
    {
        return entity.Has<VBloodUnit>();
    }
    public static User GetUser(this Entity entity)
    {
        if (entity.TryGetComponent(out User user))
        {
            return user;
        }
        else if (entity.TryGetComponent(out PlayerCharacter playerCharacter) && playerCharacter.UserEntity.TryGetComponent(out user))
        {
            return user;
        }

        return User.Empty;
    }
    public static PrefabGUID GetPrefabGuid(this Entity entity)
    {
        if (entity.TryGetComponent(out PrefabGUID prefabGuid))
        {
            return prefabGuid;
        }

        return PrefabGUID.Empty;
    }
    public static NetworkId GetNetworkId(this Entity entity)
    {
        if (entity.TryGetComponent(out NetworkId networkId))
        {
            return networkId;
        }

        return NetworkId.Empty;
    }
    public static void DumpEntity(this Entity entity)
    {
        World world = VWorld.World;
        Il2CppSystem.Text.StringBuilder sb = new();

        try
        {
            EntityDebuggingUtility.DumpEntity(world, entity, true, sb);
            VWorld.Log.LogInfo($"Entity Dump:\n{sb.ToString()}");
        }
        catch (Exception e)
        {
            VWorld.Log.LogWarning($"Error dumping entity: {e.Message}");
        }
    }
    public static EntityQuery BuildQuery(
        this EntityManager entityManager,
        ComponentType[] allTypes,
        ComponentType[] anyTypes = null,
        ComponentType[] noneTypes = null,
        EntityQueryOptions options = EntityQueryOptions.Default)
    {
        if (allTypes == null || allTypes.Length == 0)
        {
            throw new ArgumentException("AllTypes must contain at least one component!", nameof(allTypes));
        }

        EntityQueryBuilder builder = new(Allocator.Temp);
        builder.WithOptions(options);

        foreach (var componentType in allTypes)
        {
            builder.AddAll(componentType);
        }

        if (anyTypes != null)
        {
            foreach (var componentType in anyTypes)
            {
                builder.AddAny(componentType);
            }
        }

        if (noneTypes != null)
        {
            foreach (var componentType in noneTypes)
            {
                builder.AddNone(componentType);
            }
        }

        return entityManager.CreateEntityQuery(ref builder);
    }
    public static NativeAccessor<Entity> ToEntityArrayAccessor(this EntityQuery entityQuery, Allocator allocator = Allocator.Temp)
    {
        NativeArray<Entity> entities = entityQuery.ToEntityArray(allocator);
        return new(entities);
    }
    public static NativeAccessor<T> ToComponentDataArrayAccessor<T>(this EntityQuery entityQuery, Allocator allocator = Allocator.Temp) where T : unmanaged
    {
        NativeArray<T> components = entityQuery.ToComponentDataArray<T>(allocator);
        return new(components);
    }
}