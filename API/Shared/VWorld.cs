using BepInEx.Logging;
using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;

namespace Emberglass.API.Shared;

/// <summary>
/// Various utilities for interacting with the Unity ECS world.
/// </summary>
public static class VWorld
{
    enum RuntimeContext
    {
        Client,
        Server
    }

    public static EntityManager EntityManager => World.EntityManager;

    static World _clientWorld;
    static World _serverWorld;
    static RuntimeContext? _runtimeContextOverride;

    static ScriptMapper _scriptMapper;
    static GameManager _gameManager;
    static NetworkIdSystem.Singleton _networkIdSystem;

    /// <summary>
    /// Return the Unity ECS World instance used on the server build of VRising.
    /// </summary>
    /// <remarks>
    /// This property is server-only and throws if accessed on the client.
    /// </remarks>
    public static World Server
    {
        get
        {
            if (_serverWorld?.IsCreated == true)
            {
                return _serverWorld;
            }

            _serverWorld = WorldUtility.FindServerWorld()
                ?? throw new Exception("There is no Server world (yet). Did you install a server mod on the client?");
            return _serverWorld;
        }
    }

    /// <summary>
    /// Return the Unity ECS World instance used on the client build of VRising.
    /// </summary>
    /// <remarks>
    /// This property is client-only and throws if accessed on the server.
    /// </remarks>
    public static World Client
    {
        get
        {
            if (_clientWorld?.IsCreated == true)
            {
                return _clientWorld;
            }

            _clientWorld = WorldUtility.FindClientWorld()
                ?? throw new Exception("There is no Client world (yet). Did you install a client mod on the server?");
            return _clientWorld;
        }
    }

    public static ScriptMapper ScriptMapper
    {
        get
        {
            if (_scriptMapper != null)
            {
                return _scriptMapper;
            }

            ComponentSystemBase scriptMapper = IsClient
                ? World.GetExistingSystemManaged<ClientScriptMapper>()
                : World.GetExistingSystemManaged<ServerScriptMapper>();

            _scriptMapper = new ScriptMapper(scriptMapper);
            return _scriptMapper;
        }
    }

    public static GameManager GameManager
    {
        get
        {
            if (_gameManager != null)
            {
                return _gameManager;
            }

            if (IsClient)
            {
                _gameManager = new(ScriptMapper.ClientScriptMapper);
            }
            else
            {
                _gameManager = new(ScriptMapper.ServerScriptMapper);
            }

            return _gameManager;
        }
    }

    public static NetworkIdSystem.Singleton NetworkIdSystem
    {
        get
        {
            if (_networkIdSystem.Equals(default(NetworkIdSystem.Singleton)))
            {
                _networkIdSystem = GetSingleton<NetworkIdSystem.Singleton>();
            }

            return _networkIdSystem;
        }
    }

    /// <summary>
    /// Local character and user entities when running on the client build of VRising.
    /// </summary>
    /// <remarks>
    /// This property is client-only and throws if accessed on the server.
    /// </remarks>
    public static Entity LocalCharacter
    {
        get
        {
            if (_localCharacter.Exists())
            {
                return _localCharacter;
            }

            return ConsoleShared.TryGetLocalCharacterInCurrentWorld(out _localCharacter, World) && _localCharacter.Exists()
                ? _localCharacter
                : Entity.Null;
        }
    }

    /// <summary>
    /// Local user entity when running on the client build of VRising.
    /// </summary>
    /// <remarks>
    /// This property is client-only and throws if accessed on the server.
    /// </remarks>
    public static Entity LocalUser
    {
        get
        {
            if (_localUser.Exists())
            {
                return _localUser;
            }

            return ConsoleShared.TryGetLocalUserInCurrentWorld(out _localUser, World) && _localUser.Exists()
                ? _localUser
                : Entity.Null;
        }
    }

    static Entity _localCharacter;
    static Entity _localUser;
    public static World Default => World.DefaultGameObjectInjectionWorld;
    public static World World => IsClient ? Client : Server;
    public static bool IsServer
        => TryGetRuntimeContext(out RuntimeContext context)
            && context == RuntimeContext.Server;
    public static bool IsClient
        => TryGetRuntimeContext(out RuntimeContext context)
            && context == RuntimeContext.Client;
    public static ManualLogSource Log
        => Plugin.Logger;

    /// <summary>
    /// Temporarily overrides client/server context detection for testing scenarios.
    /// </summary>
    /// <param name="isClient">True to force client context; false to force server context.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous runtime context.</returns>
    internal static IDisposable BeginRuntimeContextOverride(bool isClient)
    {
        return new RuntimeContextOverrideScope(isClient ? RuntimeContext.Client : RuntimeContext.Server);
    }

    public static T GetSystem<T>() where T : ComponentSystemBase
    {
        return World.GetExistingSystemManaged<T>();
    }
    public static T GetSingleton<T>() => ScriptMapper.GetSingleton<T>();
    public static Entity GetSingletonEntity<T>() => ScriptMapper.GetSingletonEntity<T>();
    public static Entity GetSingletonEntityFromAccessor<T>()
    {
        return SingletonAccessor<T>.TryGetSingletonEntityWasteful(EntityManager, out Entity singletonEntity)
            ? singletonEntity
            : Entity.Null;
    }

    static bool TryGetRuntimeContext(out RuntimeContext context)
    {
        try
        {
            if (_runtimeContextOverride.HasValue)
            {
                context = _runtimeContextOverride.Value;
                return true;
            }

            if (TryGetRuntimeContextFromWorld(Default, out context))
            {
                return true;
            }

            if (TryGetRuntimeContextFromWorld(_clientWorld, out context)
                || TryGetRuntimeContextFromWorld(_serverWorld, out context))
            {
                return true;
            }

            World clientWorld = WorldUtility.FindClientWorld();
            if (TryGetRuntimeContextFromWorld(clientWorld, out context))
            {
                _clientWorld = clientWorld;
                return true;
            }

            World serverWorld = WorldUtility.FindServerWorld();
            if (TryGetRuntimeContextFromWorld(serverWorld, out context))
            {
                _serverWorld = serverWorld;
                return true;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or MissingMethodException or TypeLoadException)
        {
            context = default;
            return false;
        }

        string processName = Process.GetCurrentProcess().ProcessName;
        if (processName.Contains("VRisingServer", StringComparison.OrdinalIgnoreCase))
        {
            context = RuntimeContext.Server;
            return true;
        }

        if (processName.Contains("VRising", StringComparison.OrdinalIgnoreCase))
        {
            context = RuntimeContext.Client;
            return true;
        }

        context = default;
        return false;
    }

    static bool TryGetRuntimeContextFromWorld(World world, out RuntimeContext context)
    {
        if (world?.IsCreated != true)
        {
            context = default;
            return false;
        }

        if (world.IsClientWorld())
        {
            context = RuntimeContext.Client;
            return true;
        }

        if (world.IsServerWorld())
        {
            context = RuntimeContext.Server;
            return true;
        }

        context = default;
        return false;
    }

    sealed class RuntimeContextOverrideScope : IDisposable
    {
        readonly RuntimeContext? _originalContext;

        /// <summary>
        /// Initializes a new runtime context override scope.
        /// </summary>
        /// <param name="runtimeContext">Context to force while the scope is active.</param>
        internal RuntimeContextOverrideScope(RuntimeContext runtimeContext)
        {
            _originalContext = _runtimeContextOverride;
            _runtimeContextOverride = runtimeContext;
        }

        /// <summary>
        /// Restores the original runtime context override.
        /// </summary>
        public void Dispose()
        {
            _runtimeContextOverride = _originalContext;
        }
    }
}
public sealed class GameManager
{
    readonly ComponentSystemBase _impl;
    public ClientGameManager ClientGameManager => _clientGameManager
        ?? throw new InvalidOperationException("ClientGameManager is null! Is this running on the server?");
    public ServerGameManager ServerGameManager => _serverGameManager
        ?? throw new InvalidOperationException("ServerGameManager is null! Is this running on the client?");

    readonly ClientGameManager? _clientGameManager;
    readonly ServerGameManager? _serverGameManager;
    internal GameManager(ComponentSystemBase impl)
    {
        _impl = impl;

        if (VWorld.IsClient && _impl is ClientScriptMapper clientScriptMapper)
        {
            _clientGameManager = clientScriptMapper._ClientGameManager;
        }
        else if (_impl is ServerScriptMapper serverScriptMapper)
        {
            _serverGameManager = serverScriptMapper._ServerGameManager;
        }
    }
}
public sealed class ScriptMapper
{
    readonly ComponentSystemBase _impl;
    public ClientScriptMapper ClientScriptMapper => _clientScriptMapper
        ?? throw new InvalidOperationException("ClientScriptMapper is null! Is this running on the server?");
    public ServerScriptMapper ServerScriptMapper => _serverScriptMapper
        ?? throw new InvalidOperationException("ServerScriptMapper is null! Is this running on the client?");

    readonly ClientScriptMapper _clientScriptMapper;
    readonly ServerScriptMapper _serverScriptMapper;
    internal ScriptMapper(ComponentSystemBase impl)
    {
        _impl = impl;

        if (impl is ClientScriptMapper clientScriptMapper)
        {
            _clientScriptMapper = clientScriptMapper;
        }
        else if (impl is ServerScriptMapper serverScriptMapper)
        {
            _serverScriptMapper = serverScriptMapper;
        }
    }
    public T GetSingleton<T>() => _impl.GetSingleton<T>();
    public Entity GetSingletonEntity<T>() => _impl.GetSingletonEntity<T>();
}
public struct NativeAccessor<T>(NativeArray<T> array) : IDisposable where T : unmanaged
{
    NativeArray<T> _array = array;
    public T this[int index]
    {
        get => _array[index];
        set => _array[index] = value;
    }
    public int Length
        => _array.Length;
    public NativeArray<T>.Enumerator GetEnumerator()
        => _array.GetEnumerator();
    public void Dispose()
        => _array.Dispose();
}
