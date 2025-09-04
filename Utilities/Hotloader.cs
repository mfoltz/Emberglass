using Emberglass.API.Shared;
using ProjectM.Scripting;
using System.Reflection;
using Unity.Entities;

namespace Emberglass.Utilities;
internal static class Hotloader
{
    static object ResolveParam(Type paramType)
    {
        // register one-off types?
        // if (CustomResolvers.TryGetValue(paramType, out var custom)) return custom();

        // World & core ECS services
        if (paramType == typeof(World))
        {
            return VWorld.World;
        }

        if (paramType == typeof(EntityManager))
        {
            return VWorld.EntityManager;
        }

        if (paramType == typeof(ClientGameManager)
            && VWorld.IsClient)
        {
            return VWorld.GameManager;
        }

        if (paramType == typeof(ServerGameManager)
            && VWorld.IsServer)
        {
            return VWorld.GameManager;
        }

        if (paramType == typeof(ClientScriptMapper)
            && VWorld.IsClient)
        {
            return VWorld.ScriptMapper;
        }

        if (paramType == typeof(ServerScriptMapper)
            && VWorld.IsServer)
        {
            return VWorld.ScriptMapper;
        }

        // ECS System (ComponentSystemBase or SystemBase)
        if (typeof(ComponentSystemBase).IsAssignableFrom(paramType))
        {
            // VWorld always selects right world, so use its GetSystem<T>
            var getSystem = typeof(VWorld).GetMethod("GetSystem").MakeGenericMethod(paramType);
            return getSystem.Invoke(null, null);
        }

        // Singleton (System Singleton)
        if (paramType.Name.EndsWith("Singleton"))
        {
            var getSingleton = typeof(VWorld).GetMethod("GetSingleton").MakeGenericMethod(paramType);
            return getSingleton.Invoke(null, null);
        }

        /*
        // Optionally, support "GetSingletonEntity<T>()" if you want to be really aggressive:
        if (paramType.Name.EndsWith("System") || paramType.Name.EndsWith("Singleton"))
        {
            var getSingletonEntity = typeof(VWorld).GetMethod("GetSingletonEntity").MakeGenericMethod(paramType);
            if (getSingletonEntity != null)
                return getSingletonEntity.Invoke(null, null);
        }
        */

        return null;
    }

    public static void ReflectAndInitialize(Assembly modAssembly)
    {
        foreach (var type in modAssembly.GetTypes())
        {
            var init = type.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (init == null)
            {
                continue;
            }

            var args = init.GetParameters().Select(p => ResolveParam(p.ParameterType)).ToArray();

            try
            {
                init.Invoke(null, args);
                VWorld.Log.LogInfo($"[{type.FullName}.Initialize({string.Join(", ", args.Select(a => a?.GetType().Name ?? "null"))})");
            }
            catch (Exception ex)
            {
                VWorld.Log.LogError($"[Hotload] {type.FullName}.Initialize threw: {ex}");
            }
        }
    }
}
