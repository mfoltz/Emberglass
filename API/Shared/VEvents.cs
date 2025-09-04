using Emberglass.API.Client;
using Emberglass.API.Server;
using System.Collections.Concurrent;
using Unity.Entities;

namespace Emberglass.API.Shared;
public static class VEvents
{
    static bool _initialized;
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        if (VWorld.IsClient)
        {
            ClientModules.Initialize();
        }

        if (VWorld.IsServer)
        {
            ServerModules.Initialize();
        }

        _initialized = true;
    }
    public interface IGameEvent;
    public interface IGameEventModule
    {
        void Initialize();
        void Uninitialize();
    }
    public abstract class DynamicEvent : EventArgs, IGameEvent
    {
        public Entity Source { get; set; }
        public Entity Target { get; set; }

        readonly Dictionary<Type, object> _components = [];
        public void AddComponent<T>(T component) where T : struct => _components[typeof(T)] = component;
        public bool TryGetComponent<T>(out T component) where T : struct
        {
            if (_components.TryGetValue(typeof(T), out var boxed) && boxed is T cast)
            {
                component = cast;
                return true;
            }

            component = default;
            return false;
        }
    }
    public abstract class GameEvent<T> : IGameEventModule where T : IGameEvent, new ()
    {
        public delegate void EventModuleHandler(T args);
        public event EventModuleHandler EventHandler;
        protected void Raise(T args)
        {
            EventHandler?.Invoke(args);
        }
        public void Subscribe(EventModuleHandler handler) => EventHandler += handler;
        public void Unsubscribe(EventModuleHandler handler) => EventHandler -= handler;
        public virtual void Initialize() { }
        public virtual void Uninitialize() { }
    }
    public static class ModuleRegistry
    {
        static readonly ConcurrentDictionary<Type, object> _modules = [];
        internal static void Register<T>(GameEvent<T> module) where T : IGameEvent, new()
        {
            module.Initialize();
            _modules[typeof(T)] = module;
        }
        internal static void Unregister<T>(GameEvent<T> module) where T : IGameEvent, new()
        {
            _modules.TryRemove(typeof(T), out var _);
                module.Uninitialize();
        }
        internal static void Uninitialize()
        {
            foreach (var module in _modules.Values)
            {
                if (module is IGameEventModule gameEventModule)
                {
                    gameEventModule.Uninitialize();
                }
            }

            _modules.Clear();
        }
        public static void Subscribe<T>(Action<T> handler) where T : IGameEvent, new()
        {
            if (_modules.TryGetValue(typeof(T), out var module))
            {
                ((GameEvent<T>)module).Subscribe(handler.Invoke);
            }
            else
            {
                VWorld.Log.LogWarning($"[Subscribe] No registered module for event type! ({typeof(T).Name})");
            }
        }
        public static bool TryGet<T>(out GameEvent<T> module) where T : IGameEvent, new()
        {
            if (_modules.TryGetValue(typeof(T), out var result))
            {
                module = (GameEvent<T>)result;
                return true;
            }

            module = default;
            return false;
        }
    }
}
