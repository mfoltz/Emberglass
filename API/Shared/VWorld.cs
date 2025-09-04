using BepInEx.Logging;
using Emberglass;
using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Emberglass.API.Shared;

/// <summary>
/// Various utilities for interacting with the Unity ECS world.
/// </summary>
public static class VWorld
{
    public static EntityManager EntityManager => World.EntityManager;

    static World _clientWorld;
    static World _serverWorld;

    static ScriptMapper _scriptMapper;
    static GameManager _gameManager;
    static NetworkIdSystem.Singleton _networkIdSystem;

    /// <summary>
    /// Return the Unity ECS World instance used on the server build of VRising.
    /// </summary>
    public static World Server
    {
        get
        {
            if (_serverWorld?.IsCreated == true)
            {
                return _serverWorld;
            }

            _serverWorld = GetWorld("Server")
                ?? throw new Exception("There is no Server world (yet). Did you install a server mod on the client?");
            return _serverWorld;
        }
    }

    /// <summary>
    /// Return the Unity ECS World instance used on the client build of VRising.
    /// </summary>
    public static World Client
    {
        get
        {
            if (_clientWorld?.IsCreated == true)
            {
                return _clientWorld;
            }

            _clientWorld = GetWorld("Client_0")
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
    public static Entity LocalCharacter =>
        IsClient && _localCharacter.Exists()
            ? _localCharacter
            : ConsoleShared.TryGetLocalCharacterInCurrentWorld(out _localCharacter, World) && _localCharacter.Exists()
                ? _localCharacter
                : Entity.Null;
    public static Entity LocalUser =>
        IsClient && _localUser.Exists()
            ? _localUser
            : ConsoleShared.TryGetLocalUserInCurrentWorld(out _localUser, World) && _localUser.Exists()
                ? _localUser
                : Entity.Null;

    static Entity _localCharacter;
    static Entity _localUser;
    public static World Default => World.DefaultGameObjectInjectionWorld;
    public static World World => IsClient ? Client : Server;
    public static bool IsServer => Application.productName == "VRisingServer";
    public static bool IsClient => Application.productName == "VRising";
    public static ManualLogSource Log => Plugin.Logger;
    static World GetWorld(string name)
    {
        foreach (var world in World.s_AllWorlds)
        {
            if (world.Name == name)
            {
                _serverWorld = world;
                return world;
            }
        }

        return null;
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
    public int Length => _array.Length;
    public NativeArray<T>.Enumerator GetEnumerator() => _array.GetEnumerator();
    public void Dispose() => _array.Dispose();
}