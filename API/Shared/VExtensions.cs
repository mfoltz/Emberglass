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
    static EntityManager EntityManager
        => VWorld.EntityManager;

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
    /// <summary>
    /// Mutates an existing component in place when the entity has that component.
    /// </summary>
    /// <typeparam name="T">Component type to mutate.</typeparam>
    /// <param name="entity">Entity that may own the component.</param>
    /// <param name="action">Mutation callback that receives the component by reference.</param>
    public static void With<T>(this Entity entity, ActionRefHandler<T> action) where T : struct
    {
        if (!entity.Has<T>())
        {
            return;
        }

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
        if (!entity.Has<T>())
        {
            return;
        }

        EntityManager.SetComponentData(entity, componentData);
    }
    public static T Read<T>(this Entity entity) where T : struct
    {
        return EntityManager.TryGetComponentData<T>(entity, out T componentData)
            ? componentData
            : default;
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
        return entity.TryGetBuffer(out DynamicBuffer<T> dynamicBuffer)
            ? dynamicBuffer
            : default;
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
    /// <summary>
    /// Attempts to read a dynamic buffer from an existing entity.
    /// </summary>
    /// <typeparam name="T">Buffer element type.</typeparam>
    /// <param name="entity">Entity that may own the buffer.</param>
    /// <param name="dynamicBuffer">Buffer value when found.</param>
    /// <returns>True when the buffer exists and is created.</returns>
    public static bool TryGetBuffer<T>(this Entity entity, out DynamicBuffer<T> dynamicBuffer) where T : struct
    {
        dynamicBuffer = default;

        if (!entity.Exists() || !entity.Has<T>())
        {
            return false;
        }

        try
        {
            dynamicBuffer = EntityManager.GetBuffer<T>(entity);
            return dynamicBuffer.IsCreated;
        }
        catch (InvalidOperationException)
        {
            dynamicBuffer = default;
            return false;
        }
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
    /// <summary>
    /// Removes the Disabled component when the entity is currently disabled.
    /// </summary>
    /// <param name="entity">Entity to enable.</param>
    public static void Enable(this Entity entity)
    {
        if (entity.IsDisabled())
        {
            entity.Remove<Disabled>();
        }
    }
    /// <summary>
    /// Adds the Disabled component when the entity is currently enabled.
    /// </summary>
    /// <param name="entity">Entity to disable.</param>
    public static void Disable(this Entity entity)
    {
        if (!entity.IsDisabled())
        {
            entity.Add<Disabled>();
        }
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

        if (entity.TryGetComponent(out PlayerCharacter playerCharacter)
            && playerCharacter.UserEntity.TryGetComponent(out user))
        {
            return user;
        }

        return User.Empty;
    }
    /// <summary>
    /// Checks whether an entity has a User component.
    /// </summary>
    /// <param name="entity">Entity to inspect.</param>
    /// <returns>True when the entity is a user entity.</returns>
    public static bool IsUser(this Entity entity)
    {
        return entity.Has<User>();
    }
    /// <summary>
    /// Resolves the user entity from either a player character or user entity.
    /// </summary>
    /// <param name="entity">Player character or user entity.</param>
    /// <returns>The matching user entity, or Entity.Null when unavailable.</returns>
    public static Entity GetUserEntity(this Entity entity)
    {
        if (entity.TryGetComponent(out PlayerCharacter playerCharacter))
        {
            return playerCharacter.UserEntity;
        }

        return entity.IsUser()
            ? entity
            : Entity.Null;
    }
    /// <summary>
    /// Resolves the platform ID from either a player character or user entity.
    /// </summary>
    /// <param name="entity">Player character or user entity.</param>
    /// <returns>The platform ID, or zero when unavailable.</returns>
    public static ulong GetSteamId(this Entity entity)
    {
        if (entity.TryGetComponent(out PlayerCharacter playerCharacter))
        {
            return playerCharacter.UserEntity.GetUser().PlatformId;
        }

        if (entity.TryGetComponent(out User user))
        {
            return user.PlatformId;
        }

        return default;
    }
    public static User GetUser(this FromCharacter fromCharacter)
    {
        if (fromCharacter.User.TryGetComponent(out User user))
        {
            return user;
        }

        if (fromCharacter.Character.TryGetComponent(out PlayerCharacter playerCharacter)
            && playerCharacter.UserEntity.TryGetComponent(out user))
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
    /// <summary>
    /// Checks whether an index can be read from a dynamic buffer.
    /// </summary>
    /// <typeparam name="T">Buffer element type.</typeparam>
    /// <param name="buffer">Buffer to inspect.</param>
    /// <param name="index">Index to test.</param>
    /// <returns>True when the buffer is created and the index is in range.</returns>
    public static bool IsIndexWithinRange<T>(this DynamicBuffer<T> buffer, int index) where T : struct
    {
        return buffer.IsCreated
            && index >= 0
            && index < buffer.Length;
    }
    /// <summary>
    /// Reads the first dynamic buffer element when one exists.
    /// </summary>
    /// <typeparam name="T">Buffer element type.</typeparam>
    /// <param name="buffer">Buffer to inspect.</param>
    /// <returns>The first element, or default when the buffer is empty or unavailable.</returns>
    public static T FirstOrDefault<T>(this DynamicBuffer<T> buffer) where T : struct
    {
        return buffer.IsIndexWithinRange(0)
            ? buffer[0]
            : default;
    }
    public static NativeAccessor<T> ToComponentDataArrayAccessor<T>(this EntityQuery entityQuery, Allocator allocator = Allocator.Temp) where T : unmanaged
    {
        NativeArray<T> components = entityQuery.ToComponentDataArray<T>(allocator);
        return new(components);
    }
    public static NativeAccessor<ArchetypeChunk> ToArchetypeChunkAccessor(this EntityQuery entityQuery, Allocator allocator = Allocator.Temp)
    {
        NativeArray<ArchetypeChunk> chunks = entityQuery.ToArchetypeChunkArray(allocator);
        return new(chunks);
    }
}
