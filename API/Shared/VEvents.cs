using Emberglass.API.Client;
using Emberglass.API.Server;
using ProjectM;
using ProjectM.Gameplay.Systems;
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
            ClientModules.Bootstrap();
        }

        if (VWorld.IsServer)
        {
            ServerModules.Bootstrap();
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
        public void AddComponent<T>(T component) where T : struct
            => _components[typeof(T)] = component;
        public bool TryGetComponent<T>(out T component) where T : struct
        {
            if (_components.TryGetValue(typeof(T), out object boxed) && boxed is T cast)
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
        readonly Dictionary<Action<T>, EventModuleHandler> _subscriptions = [];
        protected void Raise(T args)
        {
            EventHandler?.Invoke(args);
        }
        public void Subscribe(EventModuleHandler handler) => EventHandler += handler;
        public void Unsubscribe(EventModuleHandler handler) => EventHandler -= handler;
        public void Subscribe(Action<T> handler)
        {
            if (_subscriptions.ContainsKey(handler))
            {
                return;
            }

            EventModuleHandler subscription = handler.Invoke;
            _subscriptions[handler] = subscription;
            EventHandler += subscription;
        }
        public void Unsubscribe(Action<T> handler)
        {
            if (!_subscriptions.TryGetValue(handler, out EventModuleHandler subscription))
            {
                return;
            }

            EventHandler -= subscription;
            _subscriptions.Remove(handler);
        }
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
            _modules.TryRemove(typeof(T), out object _);
                module.Uninitialize();
        }
        internal static void Uninitialize()
        {
            foreach (object module in _modules.Values)
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
            if (!TrySubscribe(handler))
            {
                VWorld.Log.LogWarning($"[Subscribe] No registered module for event type! ({typeof(T).Name})");
            }
        }

        /// <summary>
        /// Attempts to subscribe to a registered event module.
        /// </summary>
        /// <typeparam name="T">Event type.</typeparam>
        /// <param name="handler">Handler to invoke when the event is raised.</param>
        /// <returns>True when a matching module was registered and the handler was subscribed.</returns>
        public static bool TrySubscribe<T>(Action<T> handler) where T : IGameEvent, new()
        {
            if (_modules.TryGetValue(typeof(T), out object module))
            {
                ((GameEvent<T>)module).Subscribe(handler);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Unsubscribes from a registered event module.
        /// </summary>
        /// <typeparam name="T">Event type.</typeparam>
        /// <param name="handler">Handler previously passed to <see cref="Subscribe{T}(Action{T})"/>.</param>
        public static void Unsubscribe<T>(Action<T> handler) where T : IGameEvent, new()
        {
            if (!TryUnsubscribe(handler))
            {
                VWorld.Log.LogWarning($"[Unsubscribe] No registered module for event type! ({typeof(T).Name})");
            }
        }

        /// <summary>
        /// Attempts to unsubscribe from a registered event module.
        /// </summary>
        /// <typeparam name="T">Event type.</typeparam>
        /// <param name="handler">Handler previously passed to <see cref="Subscribe{T}(Action{T})"/>.</param>
        /// <returns>True when a matching module was registered and the handler was unsubscribed.</returns>
        public static bool TryUnsubscribe<T>(Action<T> handler) where T : IGameEvent, new()
        {
            if (_modules.TryGetValue(typeof(T), out object module))
            {
                ((GameEvent<T>)module).Unsubscribe(handler);
                return true;
            }

            return false;
        }
        public static bool TryGet<T>(out GameEvent<T> module) where T : IGameEvent, new()
        {
            if (_modules.TryGetValue(typeof(T), out object result))
            {
                module = (GameEvent<T>)result;
                return true;
            }

            module = default;
            return false;
        }
    }
}
