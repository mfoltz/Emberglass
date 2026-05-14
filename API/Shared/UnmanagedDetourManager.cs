using BepInEx.Logging;
using BepInEx.Unity.IL2CPP.Hook;
using HarmonyLib;
using ProjectM;
using System.Reflection;
using Unity.Entities;

namespace Emberglass.API.Shared;
public static unsafe class UnmanagedDetourManager
{
    public enum WorldKind : byte { Any, Server, Client }
    public sealed class DetourHandle : IDisposable
    {
        internal readonly long _typeHash;
        internal readonly UnmanagedSystemFunctionType _fnType;
        internal readonly DetourKind _kind;
        internal readonly DetourEntry _entry;
        internal DetourHandle(long typeHash, UnmanagedSystemFunctionType fnType, DetourKind kind, DetourEntry entry)
        {
            _typeHash = typeHash;
            _fnType = fnType;
            _kind = kind;
            _entry = entry;
        }
        public void Dispose()
            => Backend.Unregister(this);
    }
    public readonly struct DetourOptions(
        WorldKind world = WorldKind.Any,
        bool onlyWhenSystemRuns = true,
        int throttle = 0,
        int priority = 0)
    {
        public readonly WorldKind World = world;
        public readonly bool OnlyWhenSystemRuns = onlyWhenSystemRuns;   // best-effort gating (Enabled + ShouldRunSystem)
        public readonly int Throttle = throttle;                        // 0 = no throttle
        public readonly int Priority = priority;                        // higher runs earlier
        public static DetourOptions Default
            => new();
    }

    /// <summary>
    /// Register Prefix detour for a system function (OnUpdate, OnCreate, etc).
    /// </summary>
    public static DetourHandle Prefix(long typeHash, UnmanagedSystemFunctionType fnType, Delegate hook, DetourOptions? opts = null)
        => Backend.Register(typeHash, fnType, DetourKind.Prefix, hook, opts ?? DetourOptions.Default);

    /// <summary>
    /// Register Postfix detour for a system function (OnUpdate, OnCreate, etc).
    /// </summary>
    public static DetourHandle Postfix(long typeHash, UnmanagedSystemFunctionType fnType, Delegate hook, DetourOptions? opts = null)
        => Backend.Register(typeHash, fnType, DetourKind.Postfix, hook, opts ?? DetourOptions.Default);
    public static DetourHandle PrefixOnUpdate(long typeHash, Delegate hook, DetourOptions? opts = null)
        => Prefix(typeHash, UnmanagedSystemFunctionType.OnUpdate, hook, opts);
    public static DetourHandle PostfixOnUpdate(long typeHash, Delegate hook, DetourOptions? opts = null)
        => Postfix(typeHash, UnmanagedSystemFunctionType.OnUpdate, hook, opts);

    /// <summary>
    /// Must be called once early so we can observe AddSystemType and install detours.
    /// </summary>
    public static void Initialize(Harmony harmony, ManualLogSource log = null, bool debugLogTypeHashes = false)
        => Backend.Initialize(harmony, log, debugLogTypeHashes);
    public static void DisposeAll()
        => Backend.DisposeAll();

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class UnmanagedPrefix(long typeHash, UnmanagedSystemFunctionType fnType = UnmanagedSystemFunctionType.OnUpdate) : Attribute
    {
        public readonly long TypeHash = typeHash;
        public readonly UnmanagedSystemFunctionType FnType = fnType;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class UnmanagedPostfix(long typeHash, UnmanagedSystemFunctionType fnType = UnmanagedSystemFunctionType.OnUpdate) : Attribute
    {
        public readonly long TypeHash = typeHash;
        public readonly UnmanagedSystemFunctionType FnType = fnType;
    }

    /// <summary>
    /// Scan an assembly for [UnmanagedPrefix]/[UnmanagedPostfix].
    /// Prefix methods may be: bool(SystemState*), bool(), void(SystemState*), void().
    /// Postfix methods may be: void(SystemState*), void().
    /// </summary>
    public static void ScanAssembly(Assembly asm, DetourOptions? defaults = null)
    {
        var def = defaults ?? DetourOptions.Default;

        foreach (var type in asm.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var a in method.GetCustomAttributes(typeof(UnmanagedPrefix), inherit: false).Cast<UnmanagedPrefix>())
                {
                    var d = CreateDelegateForDetour(method, isPrefix: true);
                    Prefix(a.TypeHash, a.FnType, d, def);
                }

                foreach (var a in method.GetCustomAttributes(typeof(UnmanagedPostfix), inherit: false).Cast<UnmanagedPostfix>())
                {
                    var d = CreateDelegateForDetour(method, isPrefix: false);
                    Postfix(a.TypeHash, a.FnType, d, def);
                }
            }
        }
    }
    static Delegate CreateDelegateForDetour(MethodInfo method, bool isPrefix)
    {
        var parameters = method.GetParameters();
        bool takesNoParams = parameters.Length == 0;
        bool takesStatePtr = parameters.Length == 1 && parameters[0].ParameterType == typeof(SystemState*);

        if (takesNoParams)
        {
            if (method.ReturnType == typeof(bool) && isPrefix)
                return method.CreateDelegate(typeof(Func<bool>));
            if (method.ReturnType == typeof(void))
                return method.CreateDelegate(typeof(Action));
        }
        else if (takesStatePtr)
        {
            if (method.ReturnType == typeof(bool) && isPrefix)
                return method.CreateDelegate(typeof(PrefixHandler));
            if (method.ReturnType == typeof(void))
                return method.CreateDelegate(isPrefix ? typeof(StateHandler) : typeof(PostfixHandler));
        }

        string supportedSignatures = isPrefix
            ? "bool(SystemState*), bool(), void(SystemState*), void()"
            : "void(SystemState*), void()";
        string detourKind = isPrefix ? "prefix" : "postfix";

        throw new InvalidOperationException(
            $"Unsupported {detourKind} detour signature: {method.DeclaringType?.FullName}.{method.Name}. " +
            $"Supported signatures: {supportedSignatures}.");
    }
    internal enum DetourKind : byte { Prefix, Postfix }
    internal sealed class DetourEntry
    {
        public readonly DetourOptions Options;
        public readonly int Priority;

        public readonly PrefixHandler Prefix;
        public readonly PostfixHandler Postfix;
        public int LastFrame;
        public DetourEntry(PrefixHandler inv, DetourOptions opts)
        {
            Prefix = inv;
            Options = opts;
            Priority = opts.Priority;
            LastFrame = 0;
        }
        public DetourEntry(PostfixHandler inv, DetourOptions opts)
        {
            Postfix = inv;
            Options = opts;
            Priority = opts.Priority;
            LastFrame = 0;
        }
    }

    internal delegate bool PrefixHandler(SystemState* state);
    internal delegate void StateHandler(SystemState* state);
    internal delegate void PostfixHandler(SystemState* state);
    internal static class Backend
    {
        static ManualLogSource _log;
        static bool _debugLogTypeHashes;
        static bool _initialized;
        static readonly object _sync = new();

        // Registered hooks (exists even before registry sees the system type hash)
        // typeHash -> fnType -> bucket
        static readonly Dictionary<long, Dictionary<UnmanagedSystemFunctionType, Detours>> _detours = [];

        // Live detours (installed when AddSystemType is observed)
        static readonly Dictionary<(long typeHash, UnmanagedSystemFunctionType fnType), DetouredFunction> _live = [];

        // Harmony patch for function pointers
        // Threading: access to _detours/_live is synchronized on _sync. DetouredFunction.Publish
        // uses atomic swaps so hot-path invocations read snapshot arrays without locking.
        public static void Initialize(Harmony harmony, ManualLogSource log, bool debugLogTypeHashes)
        {
            if (_initialized) return;
            _initialized = true;

            _log = log;
            _debugLogTypeHashes = debugLogTypeHashes;

            harmony.PatchAll(typeof(UnmanagedSystemTypeRegistryPatch));
            _log?.LogInfo("[UnmanagedDetours] Initialized (patched UnmanagedSystemTypeRegistryData.AddSystemType).");
        }
        public static DetourHandle Register(long typeHash, UnmanagedSystemFunctionType fnType, DetourKind kind, Delegate hook, DetourOptions opts)
        {
            if (!_initialized)
                _log?.LogWarning("[UnmanagedDetours] Register called before Initialize(). Hooks will queue, but ensure Initialize runs early.");

            lock (_sync)
            {
                if (!_detours.TryGetValue(typeHash, out var perFn))
                {
                    perFn = [];
                    _detours[typeHash] = perFn;
                }

                if (!perFn.TryGetValue(fnType, out var detours))
                {
                    detours = new Detours(typeHash, fnType);
                    perFn[fnType] = detours;
                }

                DetourEntry entry;
                if (kind == DetourKind.Prefix)
                    entry = new DetourEntry(AdaptPrefix(hook), opts);
                else
                    entry = new DetourEntry(AdaptPostfix(hook), opts);

                detours.Add(kind, entry);
                if (_live.TryGetValue((typeHash, fnType), out var detoured))
                    detoured.Publish(detours);

                return new(typeHash, fnType, kind, entry);
            }
        }
        public static void Unregister(DetourHandle handle)
        {
            lock (_sync)
            {
                if (!_detours.TryGetValue(handle._typeHash, out var perFn))
                    return;

                if (!perFn.TryGetValue(handle._fnType, out var bucket))
                    return;

                bucket.Remove(handle._kind, handle._entry);
                if (_live.TryGetValue((handle._typeHash, handle._fnType), out var detoured))
                    detoured.Publish(bucket);
            }
        }
        public static void DisposeAll()
        {
            lock (_sync)
            {
                foreach (var kv in _live.Values)
                {
                    kv.Dispose();
                }

                _live.Clear();
                _detours.Clear();
            }

            _log?.LogInfo("[UnmanagedDetours] Disposed all hooks and detours.");
        }
        internal static void OnSystemTypeRegistered(long typeHash, UnmanagedComponentSystemDelegates delegates)
        {
            try
            {
                if (_debugLogTypeHashes)
                    _log?.LogInfo($"[UnmanagedDetours] Seen system typeHash={typeHash}");

                lock (_sync)
                {
                    if (!_detours.TryGetValue(typeHash, out Dictionary<UnmanagedSystemFunctionType, Detours> perFn))
                        return;

                    // Install detours for each function type
                    foreach (var (fnType, bucket) in perFn)
                    {
                        var key = (typeHash, fnType);
                        if (_live.ContainsKey(key))
                            continue;

                        nint targetPtr = ResolveFunctionPointer(fnType, delegates);
                        if (targetPtr == 0)
                        {
                            _log?.LogWarning($"[UnmanagedDetours] typeHash={typeHash} fn={fnType}: resolved targetPtr=0, skipping detour.");
                            continue;
                        }

                        DetouredFunction detoured = new(typeHash, fnType, targetPtr, _log);
                        detoured.Publish(bucket); // refresh
                        detoured.Install();       // apply

                        _live[key] = detoured;
                        _log?.LogInfo($"[UnmanagedDetours] Installed detour: typeHash={typeHash} fn={fnType} ptr=0x{targetPtr.ToString("X")}");
                    }
                }
            }
            catch (Exception e)
            {
                _log?.LogError($"[UnmanagedDetours] OnSystemTypeRegistered error: {e}");
            }
        }
        static nint ResolveFunctionPointer(UnmanagedSystemFunctionType fnType, UnmanagedComponentSystemDelegates delegates)
        {
            // - delegates.BurstFunctions.FixedElementField / ManagedFunctions.FixedElementField are fixed buffers
            // - check BurstFunctionBits to decide which to use
            int idx = (int)fnType;
            const int BURST_FUNCTION_BIT_WIDTH = sizeof(ulong) * 8;

            ulong* burstBase = &delegates.BurstFunctions.FixedElementField;
            ulong* managedBase = &delegates.ManagedFunctions.FixedElementField;

            nint* burstFns = (nint*)burstBase;
            nint* managedFns = (nint*)managedBase;

            ulong burstFunctionBits = delegates.BurstFunctionBits;
            bool burstHas = (uint)idx < BURST_FUNCTION_BIT_WIDTH
                && (burstFunctionBits & (1UL << idx)) != 0;
            return burstHas ? burstFns[idx] : managedFns[idx];
        }
        static PrefixHandler AdaptPrefix(Delegate del)
        {
            // Allowed: bool(SystemState*), bool(), void(SystemState*), void()
            if (del is PrefixHandler a)
                return a;

            if (del is Func<bool> b)
                return _ => b();

            if (del is StateHandler c)
                return s => { c(s); return true; };

            if (del is Action e)
                return _ => { e(); return true; };

            throw new InvalidOperationException($"Unsupported Prefix delegate type: {del.GetType().FullName}");
        }
        static PostfixHandler AdaptPostfix(Delegate del)
        {
            // Allowed: void(SystemState*), void()
            if (del is PostfixHandler a)
                return a;

            if (del is Action b)
                return _ => b();

            throw new InvalidOperationException($"Unsupported Postfix delegate type: {del.GetType().FullName}");
        }
    }

    /// <summary>
    /// Stores detours for a specific (typeHash, fnType) and produces sorted arrays.
    /// </summary>
    internal sealed class Detours(long typeHash, UnmanagedSystemFunctionType fnType)
    {
        public readonly long TypeHash = typeHash;
        public readonly UnmanagedSystemFunctionType FnType = fnType;

        readonly List<DetourEntry> _prefix = [];
        readonly List<DetourEntry> _postfix = [];
        public void Add(DetourKind kind, DetourEntry entry)
        {
            var list = kind == DetourKind.Prefix ? _prefix : _postfix;
            list.Add(entry);
            list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
        public void Remove(DetourKind kind, DetourEntry entry)
        {
            var list = kind == DetourKind.Prefix ? _prefix : _postfix;
            list.Remove(entry);
        }
        public DetourEntry[] GetPrefixSnapshot()
            => _prefix.Count == 0 ? [] : [.._prefix];
        public DetourEntry[] GetPostfixSnapshot()
            => _postfix.Count == 0 ? [] : [.._postfix];
    }

    /// <summary>
    /// One detour per function pointer.
    /// </summary>
    internal sealed class DetouredFunction(long typeHash, UnmanagedSystemFunctionType fnType, nint targetPtr, ManualLogSource log) : IDisposable
    {
        internal delegate void UnmanagedFnHandler(void* systemPtr, void* statePtr);

        readonly long _typeHash = typeHash;
        readonly UnmanagedSystemFunctionType _fnType = fnType;
        readonly nint _targetPtr = targetPtr;
        readonly ManualLogSource _log = log;

        // Published arrays (swapped atomically on registration changes)
        DetourEntry[] _prefix = [];
        DetourEntry[] _postfix = [];

        // Detour + GC roots
        INativeDetour _detour;
        UnmanagedFnHandler _patch;      // keep delegate alive (reverse P/Invoke thunk)
        UnmanagedFnHandler _original;   // trampoline
        public void Publish(Detours detours)
        {
            // No locks on hot path: publish snapshots via atomic reference swap.
            Volatile.Write(ref _prefix, detours.GetPrefixSnapshot());
            Volatile.Write(ref _postfix, detours.GetPostfixSnapshot());
        }
        public void Install()
        {
            _patch = PatchInvoke; // instance method group -> delegate
            _detour = INativeDetour.CreateAndApply(_targetPtr, _patch, out _original);
        }
        public void Dispose()
        {
            _detour?.Dispose();
            _detour = null;
            _patch = null;
            _original = null;
        }
        static World ResolveManagedWorld(SystemState* state)
        {
            try
            {
                var worldUnmanaged = state->WorldUnmanaged;
                foreach (World world in World.All)
                {
                    if (world?.IsCreated == true && world.Unmanaged.Equals(worldUnmanaged))
                        return world;
                }
            }
            catch
            {
                /* ignore */
            }

            return null;
        }
        static bool ShouldRun(SystemState* state)
        {
            return state->Enabled && state->ShouldRunSystem();
        }
        void PatchInvoke(void* systemPtr, void* statePtr)
        {
            var original = _original;
            if (original == null)
                return;

            // ref var state = ref Unity.Entities.Internal.InternalCompilerInterface.UnsafeGetSystemStateRef((nint)statePtr);
            SystemState* state = (SystemState*)statePtr;

            // Snapshot states
            var prefixes = Volatile.Read(ref _prefix);
            var postfixes = Volatile.Read(ref _postfix);

            // Fast exit if no detours
            if (prefixes.Length == 0 && postfixes.Length == 0)
            {
                original(systemPtr, statePtr);
                return;
            }

            // World resolution if needed
            World world = null;
            bool needsWorld = false;
            bool needsShouldRun = false;
            for (int i = 0; i < prefixes.Length; i++)
            {
                var options = prefixes[i].Options;
                if (options.World != WorldKind.Any)
                    needsWorld = true;
                if (options.OnlyWhenSystemRuns)
                    needsShouldRun = true;
            }
            for (int i = 0; i < postfixes.Length; i++)
            {
                var options = postfixes[i].Options;
                if (options.World != WorldKind.Any)
                    needsWorld = true;
                if (options.OnlyWhenSystemRuns)
                    needsShouldRun = true;
            }

            if (needsWorld)
                world = ResolveManagedWorld(state);

            bool shouldRun = true;
            if (needsShouldRun)
            {
                try
                {
                    shouldRun = ShouldRun(state);
                }
                catch
                {
                    shouldRun = true;
                }
            }

            bool runOriginal = true;

            // Prefix
            for (int i = 0; i < prefixes.Length; i++)
            {
                var h = prefixes[i];
                if (!IsValid(h, state, world, shouldRun))
                    continue;

                try
                {
                    runOriginal &= h.Prefix!(state);
                }
                catch (Exception e)
                {
                    /*
                    if (h.Options.CatchExceptions)
                        _log?.LogError($"[UnmanagedDetours] Prefix error typeHash={_typeHash} fn={_fnType}: {e}");
                    else
                        throw;
                    */

                    _log?.LogError($"[UnmanagedDetours] Prefix error typeHash={_typeHash} fn={_fnType}: {e}");
                }
            }

            // Original
            if (runOriginal)
            {
                try
                {
                    original(systemPtr, statePtr);
                }
                catch (Exception e)
                {
                    _log?.LogError($"[UnmanagedDetours] Original error typeHash={_typeHash} fn={_fnType}: {e}");
                }
            }

            // Postfix
            for (int i = 0; i < postfixes.Length; i++)
            {
                var h = postfixes[i];
                if (!IsValid(h, state, world, shouldRun))
                    continue;

                try
                {
                    h.Postfix!(state);
                }
                catch (Exception e)
                {
                    /*
                    if (h.Options.CatchExceptions)
                        _log?.LogError($"[UnmanagedDetours] Postfix error typeHash={_typeHash} fn={_fnType}: {e}");
                    else
                        throw;
                    */

                    _log?.LogError($"[UnmanagedDetours] Postfix error typeHash={_typeHash} fn={_fnType}: {e}");
                }
            }
        }
        static bool IsValid(DetourEntry entry, SystemState* state, World world, bool shouldRun)
        {
            // OnlyWhenSystemRuns
            if (entry.Options.OnlyWhenSystemRuns && !shouldRun)
                return false;

            // World filter
            if (entry.Options.World != WorldKind.Any)
            {
                if (world == null)
                    return false;

                if (entry.Options.World == WorldKind.Server)
                {
                    if (!world.IsServerWorld())
                        return false;
                }
                else if (entry.Options.World == WorldKind.Client)
                {
                    if (!world.IsClientWorld())
                        return false;
                }
            }

            // Throttle (frames)
            int throttleFrames = entry.Options.Throttle;
            if (throttleFrames > 0)
            {
                int frame = UnityEngine.Time.frameCount;
                while (true)
                {
                    int lastFrame = Volatile.Read(ref entry.LastFrame);
                    if (frame - lastFrame < throttleFrames)
                        return false;

                    if (Interlocked.CompareExchange(ref entry.LastFrame, frame, lastFrame) == lastFrame)
                        break;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(UnmanagedSystemTypeRegistryData), nameof(UnmanagedSystemTypeRegistryData.AddSystemType))]
    static class UnmanagedSystemTypeRegistryPatch
    {
        [HarmonyPostfix]
        static void Postfix(long typeHash, UnmanagedComponentSystemDelegates delegates)
            => Backend.OnSystemTypeRegistered(typeHash, delegates);
    }
}
