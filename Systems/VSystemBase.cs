using Il2CppInterop.Runtime;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;

namespace Emberglass.Systems;

/// <summary>
/// Ref-friendly query build delegate (no copying of EntityQueryBuilder).
/// </summary>
public delegate void EntityQueryHandler(ref EntityQueryBuilder builder);

/// <summary>
/// Work definition for a VSystemBase.
/// </summary>
public interface ISystemWork
{
    /// <summary>
    /// Declare queries + request handles/lookups from the builder.
    /// Runs once during system OnCreate().
    /// </summary>
    void Configure(VSystemBuilder b);

    /// <summary>Optional lifecycle hooks.</summary>
    void OnCreate(VSystemContext ctx) { }
    void OnStartRunning(VSystemContext ctx) { }
    void OnStopRunning(VSystemContext ctx) { }
    void OnDestroy(VSystemContext ctx) { }

    /// <summary>Main update loop.</summary>
    void OnUpdate(VSystemContext ctx);
}

/// <summary>
/// Minimal "updatable" contract. Builder creates these, base updates them each tick.
/// </summary>
public interface IUpdatableHandle
{
    void Update(SystemBase system);
}

/// <summary>
/// Auto-updated EntityTypeHandle wrapper.
/// </summary>
public sealed class EntityTypeHandleRef(SystemBase system) : IUpdatableHandle
{
    public EntityTypeHandle Value { get; set; } = system.GetEntityTypeHandle();
    public void Update(SystemBase system)
        => Value.Update(system);
}

/// <summary>
/// Auto-updated EntityStorageInfoLookup wrapper.
/// </summary>
public sealed class EntityStorageInfoLookupRef(SystemBase system) : IUpdatableHandle
{
    public EntityStorageInfoLookup Value { get; set; } = system.GetEntityStorageInfoLookup();
    public void Update(SystemBase system)
        => Value.Update(system);
}

/// <summary>
/// Auto-updated ComponentTypeHandle wrapper.
/// </summary>
public sealed class ComponentTypeHandleRef<T>(SystemBase system, bool readOnly) : IUpdatableHandle
    where T : unmanaged
{
    public ComponentTypeHandle<T> Value { get; set; } = system.GetComponentTypeHandle<T>(readOnly);
    public void Update(SystemBase system)
        => Value.Update(system);
}

/// <summary>
/// Auto-updated BufferTypeHandle wrapper.
/// </summary>
public sealed class BufferTypeHandleRef<T>(SystemBase system, bool readOnly) : IUpdatableHandle
    where T : unmanaged
{
    public BufferTypeHandle<T> Value { get; set; } = system.GetBufferTypeHandle<T>(readOnly);
    public void Update(SystemBase system)
        => Value.Update(system);
}

/// <summary>
/// Auto-updated ComponentLookup wrapper.
/// </summary>
public sealed class ComponentLookupRef<T>(SystemBase system, bool readOnly) : IUpdatableHandle
    where T : unmanaged
{
    public ComponentLookup<T> Value { get; set; } = system.GetComponentLookup<T>(readOnly);
    public void Update(SystemBase system)
        => Value.Update(system);
}

/// <summary>
/// Auto-updated BufferLookup wrapper.
/// </summary>
public sealed class BufferLookupRef<T>(SystemBase system, bool readOnly) : IUpdatableHandle
    where T : unmanaged
{
    public BufferLookup<T> Value { get; set; } = system.GetBufferLookup<T>(readOnly);
    public void Update(SystemBase system)
        => Value.Update(system);
}

/// <summary>
/// Builder used by Work.Configure(...) to declare queries and request handles/lookups.
/// </summary>
public sealed class VSystemBuilder
{
    readonly SystemBase _system;
    readonly List<IUpdatableHandle> _updatables;
    readonly List<Action<SystemBase>> _refreshActions;
    readonly List<JobPlanEntry> _jobPlans = [];
    readonly NativeResourceRegistry nativeResources;

    QuerySpec? _main;
    readonly Dictionary<string, QuerySpec> _named = new(StringComparer.Ordinal);
    internal VSystemBuilder(
        SystemBase system,
        List<IUpdatableHandle> updatables,
        List<Action<SystemBase>> refreshActions,
        NativeResourceRegistry nativeResourceRegistry)
    {
        _system = system;
        _updatables = updatables;
        _refreshActions = refreshActions;
        nativeResources = nativeResourceRegistry;
    }
    public void MainQuery(EntityQueryHandler build, bool requireForUpdate = true, EntityQueryOptions? options = null)
        => _main = new QuerySpec("Main", build, requireForUpdate, options);
    public void Query(string name, EntityQueryHandler build, bool requireForUpdate = false, EntityQueryOptions? options = null)
        => _named[name] = new QuerySpec(name, build, requireForUpdate, options);

    /// <summary>
    /// Register a chunk job entry for deterministic per-chunk execution against a named query.
    /// </summary>
    /// <typeparam name="TJob">Chunk job type whose fields will be inspected and refreshed.</typeparam>
    /// <param name="queryName">Query name to associate with the chunk job plan.</param>
    /// <param name="options">Optional job planning settings.</param>
    public void ChunkJob<TJob>(string queryName = "Main", JobPlanOptions options = null)
        where TJob : struct, IChunkJob
        => AddJobPlan<TJob>(queryName, options);

    /// <summary>
    /// Register an experimental planned job entry for deterministic runtime binding.
    /// </summary>
    /// <typeparam name="TJob">Job type whose fields will be inspected.</typeparam>
    /// <param name="queryName">Query name to associate with the job plan.</param>
    /// <param name="options">Optional job planning settings.</param>
    [Obsolete("Use ChunkJob<TJob> for chunk iteration. Non-chunk planned jobs remain experimental.")]
    public void FauxJob<TJob>(string queryName = "Main", JobPlanOptions options = null)
        => AddJobPlan<TJob>(queryName, options);

    void AddJobPlan<TJob>(string queryName, JobPlanOptions options)
    {
        var metadata = JobFieldInspector.Inspect(typeof(TJob));
        _jobPlans.Add(new JobPlanEntry(typeof(TJob), queryName, options, metadata));
        nativeResources.Register(metadata.NativeResourceFields);
    }

    // ---- Handle/Lookup factories (auto-updated) ----
    public EntityTypeHandleRef EntityTypeHandle()
    {
        EntityTypeHandleRef h = new(_system);
        _updatables.Add(h);
        return h;
    }
    public EntityStorageInfoLookupRef EntityStorageInfoLookup()
    {
        EntityStorageInfoLookupRef l = new(_system);
        _updatables.Add(l);
        return l;
    }
    public ComponentTypeHandleRef<T> ComponentTypeHandle<T>(bool readOnly = true)
        where T : unmanaged
    {
        ComponentTypeHandleRef<T> h = new(_system, readOnly);
        _updatables.Add(h);
        return h;
    }
    public BufferTypeHandleRef<T> BufferTypeHandle<T>(bool readOnly = true)
        where T : unmanaged
    {
        BufferTypeHandleRef<T> h = new(_system, readOnly);
        _updatables.Add(h);
        return h;
    }
    public ComponentLookupRef<T> ComponentLookup<T>(bool readOnly = true)
        where T : unmanaged
    {
        ComponentLookupRef<T> l = new(_system, readOnly);
        _updatables.Add(l);
        return l;
    }
    public BufferLookupRef<T> BufferLookup<T>(bool readOnly = true)
        where T : unmanaged
    {
        BufferLookupRef<T> l = new(_system, readOnly);
        _updatables.Add(l);
        return l;
    }

    /// <summary>
    /// For anything custom that must refresh each tick (eg cached service refs, custom filters, etc).
    /// </summary>
    public void RefreshEachTick(Action<SystemBase> refresh)
        => _refreshActions.Add(refresh);
    internal QuerySpec MainSpecOrThrow()
        => _main ?? throw new InvalidOperationException("Work.Configure must call MainQuery(...)");
    internal IReadOnlyDictionary<string, QuerySpec> NamedSpecs
        => _named;
    internal IReadOnlyList<JobPlanEntry> JobPlans
        => _jobPlans;
    public readonly struct QuerySpec
    {
        public string Name { get; }
        public EntityQueryHandler Build { get; }
        public bool RequireForUpdate { get; }
        public EntityQueryOptions? Options { get; }
        internal QuerySpec(string name, EntityQueryHandler build, bool requireForUpdate, EntityQueryOptions? options)
        {
            Name = name;
            Build = build;
            RequireForUpdate = requireForUpdate;
            Options = options;
        }
    }

    /*
    internal readonly record struct QuerySpec(
        string Name,
        QueryBuildHandler Build,
        bool RequireForUpdate,
        EntityQueryOptions? Options
    );
    */
}

/// <summary>
/// Optional settings that influence how job plans are interpreted at runtime.
/// </summary>
public sealed class JobPlanOptions
{
    /// <summary>
    /// Gets or sets whether native resources should ensure capacity based on query entity count.
    /// </summary>
    public bool EnsureCapacityFromQuery { get; set; }
}

/// <summary>
/// Captures a planned job, including the query binding and inspected field metadata.
/// </summary>
public sealed class JobPlanEntry
{
    public Type JobType { get; }
    public string QueryName { get; }
    public JobPlanOptions Options { get; }
    public JobFieldMetadata Metadata { get; }
    internal JobPlanEntry(Type jobType, string queryName, JobPlanOptions options, JobFieldMetadata metadata)
    {
        JobType = jobType;
        QueryName = queryName;
        Options = options;
        Metadata = metadata;
    }
}

/// <summary>
/// Categorized job field metadata for deterministic runtime binding.
/// </summary>
public sealed class JobFieldMetadata
{
    public IReadOnlyList<JobFieldInfo> UpdatableFields { get; }
    public IReadOnlyList<JobFieldInfo> NativeResourceFields { get; }
    internal JobFieldMetadata(IReadOnlyList<JobFieldInfo> updatableFields, IReadOnlyList<JobFieldInfo> nativeResourceFields)
    {
        UpdatableFields = updatableFields;
        NativeResourceFields = nativeResourceFields;
    }
}

/// <summary>
/// Describes a single job field and its binding category.
/// </summary>
public sealed class JobFieldInfo
{
    public FieldInfo Field { get; }
    public JobFieldCategory Category { get; }
    public JobFieldAccessMode AccessMode { get; }
    public Type ValueType { get; }
    /// <summary>
    /// Gets the concrete native resource type for the field, if applicable.
    /// </summary>
    public Type ResourceType { get; }

    internal JobFieldInfo(
        FieldInfo field,
        JobFieldCategory category,
        JobFieldAccessMode accessMode,
        Type valueType,
        Type resourceType = null)
    {
        Field = field;
        Category = category;
        AccessMode = accessMode;
        ValueType = valueType;
        ResourceType = resourceType;
    }
}

/// <summary>
/// Categories for recognized job fields.
/// </summary>
public enum JobFieldCategory
{
    UpdatableHandle,
    NativeResource
}

/// <summary>
/// Declares whether a field should be treated as read-only or read-write.
/// </summary>
public enum JobFieldAccessMode
{
    ReadOnly,
    ReadWrite
}

/// <summary>
/// Inspects job fields and caches categorized metadata for reuse.
/// </summary>
/// <remarks>
/// Job fields may use <see cref="RO{T}"/> and <see cref="RW{T}"/> wrappers to declare intent
/// without attributes. For example, <c>RO&lt;ComponentLookup&lt;Health&gt;&gt;</c> or
/// <c>RW&lt;NativeParallelHashSet&lt;Entity&gt;&gt;</c> let the inspector infer access and select
/// read-only vs writer views for native containers.
/// </remarks>
public static class JobFieldInspector
{
    static readonly Dictionary<Type, JobFieldMetadata> Cache = new();
    static readonly object CacheLock = new();

    /// <summary>
    /// Inspect a job type and return categorized field metadata.
    /// </summary>
    /// <param name="jobType">Job type to inspect.</param>
    /// <returns>Cached metadata for the job type.</returns>
    public static JobFieldMetadata Inspect(Type jobType)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(jobType, out var metadata))
            {
                return metadata;
            }

            var fields = jobType.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(field => field.MetadataToken)
                .ToArray();
            List<JobFieldInfo> updatableFields = new();
            List<JobFieldInfo> nativeResourceFields = new();

            foreach (var field in fields)
            {
                var accessInfo = ResolveFieldAccess(field.FieldType);
                if (IsUpdatableHandleType(accessInfo.FieldType))
                {
                    updatableFields.Add(new JobFieldInfo(
                        field,
                        JobFieldCategory.UpdatableHandle,
                        accessInfo.AccessMode,
                        accessInfo.FieldType));
                }
                else if (IsNativeResourceType(accessInfo.FieldType))
                {
                    var viewType = ResolveNativeResourceViewType(accessInfo.FieldType, accessInfo.AccessMode);
                    var resourceType = ResolveNativeResourceContainerType(accessInfo.FieldType);
                    nativeResourceFields.Add(new JobFieldInfo(
                        field,
                        JobFieldCategory.NativeResource,
                        accessInfo.AccessMode,
                        viewType,
                        resourceType));
                }
            }

            metadata = new JobFieldMetadata(updatableFields, nativeResourceFields);
            Cache[jobType] = metadata;
            return metadata;
        }
    }
    readonly struct FieldAccessInfo(Type fieldType, JobFieldAccessMode accessMode)
    {
        public Type FieldType { get; } = fieldType;
        public JobFieldAccessMode AccessMode { get; } = accessMode;
    }

    static FieldAccessInfo ResolveFieldAccess(Type fieldType)
    {
        if (TryUnwrapAccessWrapper(fieldType, out var wrappedType, out var accessMode))
        {
            return new FieldAccessInfo(wrappedType, accessMode);
        }

        return new FieldAccessInfo(fieldType, InferAccessMode(fieldType));
    }

    static bool TryUnwrapAccessWrapper(Type fieldType, out Type wrappedType, out JobFieldAccessMode accessMode)
    {
        wrappedType = fieldType;
        accessMode = JobFieldAccessMode.ReadWrite;

        if (!fieldType.IsGenericType)
        {
            return false;
        }

        var genericDefinition = fieldType.GetGenericTypeDefinition();
        if (genericDefinition == typeof(RO<>))
        {
            wrappedType = fieldType.GetGenericArguments()[0];
            accessMode = JobFieldAccessMode.ReadOnly;
            return true;
        }

        if (genericDefinition == typeof(RW<>))
        {
            wrappedType = fieldType.GetGenericArguments()[0];
            accessMode = JobFieldAccessMode.ReadWrite;
            return true;
        }

        return false;
    }

    static JobFieldAccessMode InferAccessMode(Type fieldType)
    {
        if (IsReadOnlyViewType(fieldType))
        {
            return JobFieldAccessMode.ReadOnly;
        }

        if (IsParallelWriterType(fieldType))
        {
            return JobFieldAccessMode.ReadWrite;
        }

        return JobFieldAccessMode.ReadWrite;
    }

    static bool IsUpdatableHandleType(Type fieldType)
    {
        if (fieldType == typeof(EntityTypeHandle))
        {
            return true;
        }

        if (fieldType == typeof(EntityStorageInfoLookup))
        {
            return true;
        }

        if (!fieldType.IsGenericType)
        {
            return false;
        }

        var genericDefinition = fieldType.GetGenericTypeDefinition();
        return genericDefinition == typeof(ComponentTypeHandle<>)
            || genericDefinition == typeof(BufferTypeHandle<>)
            || genericDefinition == typeof(ComponentLookup<>)
            || genericDefinition == typeof(BufferLookup<>);
    }

    static bool IsNativeResourceType(Type fieldType)
    {
        if (fieldType.IsGenericType)
        {
            var genericDefinition = fieldType.GetGenericTypeDefinition();
            if (genericDefinition == typeof(NativeParallelHashSet<>)
                || genericDefinition == typeof(NativeParallelMultiHashMap<,>))
            {
                return true;
            }
        }

        if (!fieldType.IsNested)
        {
            return false;
        }

        if (fieldType.Name != "ParallelWriter" && fieldType.Name != "ReadOnly")
        {
            return false;
        }

        var declaringType = fieldType.DeclaringType;
        if (declaringType == null || !declaringType.IsGenericType)
        {
            return false;
        }

        var declaringDefinition = declaringType.GetGenericTypeDefinition();
        return declaringDefinition == typeof(NativeParallelHashSet<>)
            || declaringDefinition == typeof(NativeParallelMultiHashMap<,>);
    }

    static bool IsReadOnlyViewType(Type fieldType)
        => fieldType.IsNested && fieldType.Name == "ReadOnly";

    static bool IsParallelWriterType(Type fieldType)
        => fieldType.IsNested && fieldType.Name == "ParallelWriter";

    /// <summary>
    /// Resolves the container type for a native resource field.
    /// </summary>
    /// <param name="fieldType">The field type to inspect.</param>
    /// <returns>The concrete native resource container type.</returns>
    static Type ResolveNativeResourceContainerType(Type fieldType)
    {
        if (fieldType.IsGenericType)
        {
            var genericDefinition = fieldType.GetGenericTypeDefinition();
            if (genericDefinition == typeof(NativeParallelHashSet<>)
                || genericDefinition == typeof(NativeParallelMultiHashMap<,>))
            {
                return fieldType;
            }
        }

        var declaringType = fieldType.DeclaringType;
        return declaringType ?? fieldType;
    }

    static Type ResolveNativeResourceViewType(Type fieldType, JobFieldAccessMode accessMode)
    {
        if (IsReadOnlyViewType(fieldType) || IsParallelWriterType(fieldType))
        {
            return fieldType;
        }

        if (!fieldType.IsGenericType)
        {
            return fieldType;
        }

        var genericDefinition = fieldType.GetGenericTypeDefinition();
        if (genericDefinition != typeof(NativeParallelHashSet<>)
            && genericDefinition != typeof(NativeParallelMultiHashMap<,>))
        {
            return fieldType;
        }

        string viewName = accessMode == JobFieldAccessMode.ReadOnly ? "ReadOnly" : "ParallelWriter";
        var viewType = fieldType.GetNestedType(viewName, BindingFlags.Public | BindingFlags.NonPublic);
        return viewType ?? fieldType;
    }
}

/// <summary>
/// Context passed to work code. Holds a reference to the system (no per-frame delegate creation).
/// </summary>
public sealed class VSystemContext
{
    readonly VSystemBase _system;
    internal VSystemContext(VSystemBase system)
        => _system = system;
    public SystemBase System
        => _system;
    public EntityManager EntityManager
        => _system.EntityManager;
    public EntityQuery MainQuery
        => _system.MainQuery;
    public EntityQuery GetQuery(string name)
        => _system.GetQuery(name);
    public bool Exists(Entity e)
        => _system.EntityExists(e);

    // Utilities (Temp allocations + deterministic dispose)
    public static void WithTempEntities(EntityQuery q, Action<NativeArray<Entity>> action)
        => VSystemBase.WithTempEntities(q, action);
    public static void WithTempChunks(EntityQuery q, Action<NativeArray<ArchetypeChunk>> action)
        => VSystemBase.WithTempChunks(q, action);
    public static void ForEachEntity(EntityQuery q, Action<Entity> action)
        => VSystemBase.ForEachEntity(q, action);
    public static void ForEachChunk(EntityQuery q, Action<ArchetypeChunk> action)
        => VSystemBase.ForEachChunk(q, action);
}

/// <summary>
/// Non-generic base so the context can safely reference it.
/// </summary>
public abstract class VSystemBase : SystemBase
{
    static readonly Dictionary<Type, MethodInfo> ChunkJobRunnerCache = new();
    static readonly object ChunkJobRunnerLock = new();
    static readonly Dictionary<Type, MethodInfo> JobRunnerCache = new();
    static readonly object JobRunnerLock = new();
    readonly Dictionary<string, EntityQuery> _queries = new(StringComparer.Ordinal);
    readonly List<IUpdatableHandle> _updatables = [];
    readonly List<Action<SystemBase>> _refreshActions = [];
    readonly NativeResourceRegistry nativeResources = new();

    VSystemContext _ctx;
    internal EntityQuery MainQuery { get; private set; }
    IReadOnlyList<JobPlanEntry> jobPlans = Array.Empty<JobPlanEntry>();
    internal EntityQuery GetQuery(string name)
        => _queries.TryGetValue(name, out var q) ? q : throw new KeyNotFoundException($"No query named '{name}'.");
    internal bool EntityExists(Entity e) => _storage.Value.Exists(e);

    // Always-available core safety (also auto-updated)
    EntityStorageInfoLookupRef _storage = null!;
    public sealed override void OnCreate()
    {
        base.OnCreate();

        _storage = new EntityStorageInfoLookupRef(this);
        _updatables.Add(_storage);

        _ctx = new VSystemContext(this);

        // Let generic subclass build queries + register updatables/refresh.
        BuildFromWork(_updatables, _refreshActions, nativeResources);

        // Prime once so OnCreate hooks can use handles/lookups safely.
        UpdateUpdatablesAndRefresh();

        OnWorkCreate(_ctx);
    }
    public sealed override void OnStartRunning()
    {
        base.OnStartRunning();
        if (_ctx != null) OnWorkStartRunning(_ctx);
    }
    public sealed override void OnStopRunning()
    {
        if (_ctx != null) OnWorkStopRunning(_ctx);
        base.OnStopRunning();
    }
    public sealed override void OnDestroy()
    {
        if (_ctx != null) OnWorkDestroy(_ctx);
        nativeResources.DisposeResources();
        base.OnDestroy();
    }
    public sealed override void OnUpdate()
    {
        UpdateUpdatablesAndRefresh();
        RunPlans(_ctx!);
        OnWorkUpdate(_ctx!);
    }
    void UpdateUpdatablesAndRefresh()
        => RefreshUpdatablesAndActions(_updatables, _refreshActions, this);

    /// <summary>
    /// Refreshes registered handles/lookups before executing custom refresh callbacks.
    /// </summary>
    /// <param name="updatables">Registered handles and lookups to update.</param>
    /// <param name="refreshActions">Custom refresh callbacks to run after handle refresh.</param>
    /// <param name="system">System instance used to refresh ECS handles.</param>
    internal static void RefreshUpdatablesAndActions(
        IReadOnlyList<IUpdatableHandle> updatables,
        IReadOnlyList<Action<SystemBase>> refreshActions,
        SystemBase system)
    {
        // Update all requested handles/lookups
        for (int i = 0; i < updatables.Count; i++)
            updatables[i].Update(system);

        // Run custom refresh actions
        for (int i = 0; i < refreshActions.Count; i++)
            refreshActions[i](system);
    }

    /// <summary>
    /// Executes per-update job plans, including resource maintenance and query execution.
    /// </summary>
    /// <param name="ctx">The system context for query access.</param>
    protected void RunPlans(VSystemContext ctx)
    {
        if (jobPlans.Count == 0)
        {
            return;
        }

        nativeResources.ClearEachUpdateResources();

        for (int i = 0; i < jobPlans.Count; i++)
        {
            var plan = jobPlans[i];
            var query = ctx.GetQuery(plan.QueryName);

            if (plan.Options?.EnsureCapacityFromQuery == true && plan.Metadata.NativeResourceFields.Count > 0)
            {
                int needed = query.CalculateEntityCount();
                nativeResources.EnsureCapacity(plan.Metadata.NativeResourceFields, needed);
            }

            object job = CreateJobInstance(plan);
            RunPlannedJob(plan, query, job);
        }
    }

    /// <summary>
    /// Creates and binds a job instance for the provided plan.
    /// </summary>
    /// <param name="plan">The job plan metadata.</param>
    /// <returns>The boxed job instance.</returns>
    object CreateJobInstance(JobPlanEntry plan)
    {
        if (!plan.JobType.IsValueType)
        {
            throw new InvalidOperationException($"Job plan type '{plan.JobType}' must be a struct.");
        }

        object job = Activator.CreateInstance(plan.JobType)
            ?? throw new InvalidOperationException($"Failed to create job '{plan.JobType}'.");

        AssignUpdatableFields(job, plan.Metadata.UpdatableFields);
        AssignNativeResourceFields(job, plan.Metadata.NativeResourceFields);
        return job;
    }

    /// <summary>
    /// Assigns updatable handle values to the boxed job instance.
    /// </summary>
    /// <param name="job">The boxed job instance.</param>
    /// <param name="fields">Updatable field metadata.</param>
    void AssignUpdatableFields(object job, IReadOnlyList<JobFieldInfo> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            var fieldInfo = fields[i];
            object assignedValue = ResolveUpdatableFieldValue(fieldInfo);
            fieldInfo.Field.SetValue(job, assignedValue);
        }
    }

    /// <summary>
    /// Assigns native resource values to the boxed job instance.
    /// </summary>
    /// <param name="job">The boxed job instance.</param>
    /// <param name="fields">Native resource field metadata.</param>
    void AssignNativeResourceFields(object job, IReadOnlyList<JobFieldInfo> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            var fieldInfo = fields[i];
            object assignedValue = ResolveNativeResourceFieldValue(fieldInfo);
            fieldInfo.Field.SetValue(job, assignedValue);
        }
    }

    /// <summary>
    /// Resolves the value to assign for an updatable job field.
    /// </summary>
    /// <param name="fieldInfo">The job field metadata.</param>
    /// <returns>The value to assign.</returns>
    object ResolveUpdatableFieldValue(JobFieldInfo fieldInfo)
    {
        var fieldType = fieldInfo.Field.FieldType;
        if (TryGetAccessWrapperValueType(fieldType, out var wrappedType))
        {
            object value = GetRequiredUpdatableValue(_updatables, wrappedType);
            return Activator.CreateInstance(fieldType, value)
                ?? throw new InvalidOperationException($"Failed to create wrapper '{fieldType}'.");
        }

        return GetRequiredUpdatableValue(_updatables, fieldInfo.ValueType);
    }

    /// <summary>
    /// Resolves the value to assign for a native resource job field.
    /// </summary>
    /// <param name="fieldInfo">The job field metadata.</param>
    /// <returns>The value to assign.</returns>
    object ResolveNativeResourceFieldValue(JobFieldInfo fieldInfo)
    {
        var fieldType = fieldInfo.Field.FieldType;
        if (TryGetAccessWrapperValueType(fieldType, out var wrappedType))
        {
            object value = nativeResources.GetResourceValue(fieldInfo, wrappedType);
            return Activator.CreateInstance(fieldType, value)
                ?? throw new InvalidOperationException($"Failed to create wrapper '{fieldType}'.");
        }

        return nativeResources.GetResourceValue(fieldInfo, fieldInfo.ValueType);
    }

    /// <summary>
    /// Executes a planned job against the specified query.
    /// </summary>
    /// <param name="plan">The job plan metadata.</param>
    /// <param name="query">The query to execute against.</param>
    /// <param name="boxedJob">The boxed job instance.</param>
    void RunPlannedJob(JobPlanEntry plan, EntityQuery query, object boxedJob)
    {
        var runner = typeof(VSystemBase).GetMethod(nameof(RunPlannedJobInternal), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing job runner method.");
        var genericRunner = runner.MakeGenericMethod(plan.JobType);
        genericRunner.Invoke(this, new object[] { query, boxedJob });
    }

    /// <summary>
    /// Executes a planned job for a specific job type.
    /// </summary>
    /// <typeparam name="TJob">The job type.</typeparam>
    /// <param name="query">The query to run.</param>
    /// <param name="boxedJob">The boxed job instance.</param>
    void RunPlannedJobInternal<TJob>(EntityQuery query, object boxedJob)
        where TJob : struct
    {
        TJob job = (TJob)boxedJob;
        if (job is IChunkJob)
        {
            RunChunkJob(query, ref job);
            return;
        }

        RunJobEntity(query, ref job);
    }

    static void RunChunkJob<TJob>(EntityQuery query, ref TJob job)
        where TJob : struct
    {
        var runner = GetChunkJobRunner(typeof(TJob))
            ?? throw new InvalidOperationException($"No chunk job runner found for '{typeof(TJob)}'.");

        object[] arguments = new object[] { query, job };
        runner.Invoke(null, arguments);
        job = (TJob)arguments[1];
    }

    static MethodInfo GetChunkJobRunner(Type jobType)
    {
        lock (ChunkJobRunnerLock)
        {
            if (ChunkJobRunnerCache.TryGetValue(jobType, out var cached))
            {
                return cached;
            }

            var runMethod = typeof(ChunkJobExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => method.Name == "Run" && method.IsGenericMethodDefinition);

            if (runMethod is null)
            {
                return null;
            }

            var boundMethod = runMethod.MakeGenericMethod(jobType);
            ChunkJobRunnerCache[jobType] = boundMethod;
            return boundMethod;
        }
    }

    static void RunJobEntity<TJob>(EntityQuery query, ref TJob job)
        where TJob : struct
    {
        var runner = GetJobEntityRunner(typeof(TJob))
            ?? throw new InvalidOperationException($"No job runner found for '{typeof(TJob)}'.");

        var parameters = runner.GetParameters();
        object[] arguments = new object[parameters.Length];
        int jobIndex = parameters[0].ParameterType == typeof(EntityQuery) ? 1 : 0;
        int queryIndex = 1 - jobIndex;

        arguments[jobIndex] = job;
        arguments[queryIndex] = query;

        runner.Invoke(null, arguments);
        job = (TJob)arguments[jobIndex];
    }

    static MethodInfo GetJobEntityRunner(Type jobType)
    {
        lock (JobRunnerLock)
        {
            if (JobRunnerCache.TryGetValue(jobType, out var cached))
            {
                return cached;
            }

            var extensionsType = typeof(EntityQuery).Assembly.GetType("Unity.Entities.JobEntityExtensions");
            if (extensionsType is null)
            {
                return null;
            }

            var runMethod = extensionsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => IsJobEntityRunMethod(method));

            if (runMethod is null)
            {
                return null;
            }

            var boundMethod = runMethod.MakeGenericMethod(jobType);
            JobRunnerCache[jobType] = boundMethod;
            return boundMethod;
        }
    }

    static bool IsJobEntityRunMethod(MethodInfo method)
    {
        if (method.Name != "Run" || !method.IsGenericMethodDefinition)
        {
            return false;
        }

        var parameters = method.GetParameters();
        if (parameters.Length != 2)
        {
            return false;
        }

        var jobParameter = parameters.FirstOrDefault(parameter => parameter.ParameterType.IsGenericParameter
            || (parameter.ParameterType.IsByRef && parameter.ParameterType.GetElementType()?.IsGenericParameter == true));

        return jobParameter is not null
            && parameters.Any(parameter => parameter.ParameterType == typeof(EntityQuery));
    }

    /// <summary>
    /// Attempts to unwrap RO/RW field types to their inner value type.
    /// </summary>
    /// <param name="fieldType">The job field type.</param>
    /// <param name="wrappedType">The wrapped value type.</param>
    /// <returns>True if the field type is a wrapper.</returns>
    static bool TryGetAccessWrapperValueType(Type fieldType, out Type wrappedType)
    {
        wrappedType = fieldType;
        if (!fieldType.IsGenericType)
        {
            return false;
        }

        var genericDefinition = fieldType.GetGenericTypeDefinition();
        if (genericDefinition != typeof(RO<>) && genericDefinition != typeof(RW<>))
        {
            return false;
        }

        wrappedType = fieldType.GetGenericArguments()[0];
        return true;
    }

    /// <summary>
    /// Gets an updatable handle value matching the requested type.
    /// </summary>
    /// <param name="requestedType">The requested value type.</param>
    /// <returns>The updatable handle value.</returns>
    internal static object GetRequiredUpdatableValue(
        IReadOnlyList<IUpdatableHandle> updatables,
        Type requestedType)
    {
        for (int i = 0; i < updatables.Count; i++)
        {
            var updatable = updatables[i];
            if (requestedType == typeof(EntityTypeHandle) && updatable is EntityTypeHandleRef entityTypeHandle)
            {
                return entityTypeHandle.Value;
            }

            if (requestedType == typeof(EntityStorageInfoLookup) && updatable is EntityStorageInfoLookupRef storageInfoLookup)
            {
                return storageInfoLookup.Value;
            }

            var updatableType = updatable.GetType();
            if (!updatableType.IsGenericType)
            {
                continue;
            }

            var genericDefinition = updatableType.GetGenericTypeDefinition();
            if (genericDefinition != typeof(ComponentTypeHandleRef<>)
                && genericDefinition != typeof(BufferTypeHandleRef<>)
                && genericDefinition != typeof(ComponentLookupRef<>)
                && genericDefinition != typeof(BufferLookupRef<>))
            {
                continue;
            }

            var valueProperty = updatableType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            object value = valueProperty?.GetValue(updatable);
            if (value != null && requestedType.IsAssignableFrom(value.GetType()))
            {
                return value;
            }
        }

        throw new InvalidOperationException(
            $"No updatable handle registered for '{requestedType}'. Declare it in Configure with "
            + "VSystemBuilder.EntityTypeHandle(), EntityStorageInfoLookup(), ComponentTypeHandle<T>(), "
            + "BufferTypeHandle<T>(), ComponentLookup<T>(), or BufferLookup<T>() before using it in a planned job field.");
    }

    // ---- Temp iteration helpers (Allocator.Temp + try/finally) ----
    internal static void WithTempEntities(EntityQuery q, Action<NativeArray<Entity>> action)
    {
        var arr = q.ToEntityArray(Allocator.Temp);
        try { action(arr); }
        finally { if (arr.IsCreated) arr.Dispose(); }
    }
    internal static void WithTempChunks(EntityQuery q, Action<NativeArray<ArchetypeChunk>> action)
    {
        var arr = q.ToArchetypeChunkArray(Allocator.Temp);
        try { action(arr); }
        finally { if (arr.IsCreated) arr.Dispose(); }
    }
    internal static void ForEachEntity(EntityQuery q, Action<Entity> action)
        => WithTempEntities(q, entities =>
        {
            for (int i = 0; i < entities.Length; i++) action(entities[i]);
        });
    internal static void ForEachChunk(EntityQuery q, Action<ArchetypeChunk> action)
        => WithTempChunks(q, chunks =>
        {
            for (int i = 0; i < chunks.Length; i++) action(chunks[i]);
        });

    // ---- Hooks implemented by generic subclass ----
    protected abstract void BuildFromWork(
        List<IUpdatableHandle> updatables,
        List<Action<SystemBase>> refreshActions,
        NativeResourceRegistry nativeResourceRegistry);
    protected abstract void OnWorkCreate(VSystemContext ctx);
    protected abstract void OnWorkStartRunning(VSystemContext ctx);
    protected abstract void OnWorkStopRunning(VSystemContext ctx);
    protected abstract void OnWorkDestroy(VSystemContext ctx);
    protected abstract void OnWorkUpdate(VSystemContext ctx);

    // ---- Query creation helpers for subclass ----
    protected EntityQuery CreateQuery(VSystemBuilder.QuerySpec spec)
    {
        EntityQueryBuilder b = new(Allocator.Temp);
        try
        {
            spec.Build(ref b);
            if (spec.Options.HasValue)
                b.WithOptions(spec.Options.Value);

            return EntityManager.CreateEntityQuery(ref b);
        }
        finally
        {
            b.Dispose();
        }
    }
    protected void InstallQueries(VSystemBuilder.QuerySpec main, IReadOnlyDictionary<string, VSystemBuilder.QuerySpec> named)
    {
        MainQuery = CreateQuery(main);
        if (main.RequireForUpdate) RequireForUpdate(MainQuery);

        _queries["Main"] = MainQuery;

        foreach (var kvp in named)
        {
            var q = CreateQuery(kvp.Value);
            _queries[kvp.Key] = q;
            if (kvp.Value.RequireForUpdate) RequireForUpdate(q);
        }
    }

    /// <summary>
    /// Stores the job plans that will be executed each update.
    /// </summary>
    /// <param name="jobPlans">The planned jobs for this system.</param>
    protected void InstallJobPlans(IReadOnlyList<JobPlanEntry> jobPlans)
        => this.jobPlans = jobPlans;

    /// <summary>
    /// Creates a work object and installs its query and job plan metadata.
    /// </summary>
    /// <typeparam name="TWork">The concrete work type to install.</typeparam>
    /// <param name="updatables">The updatable handles owned by the system.</param>
    /// <param name="refreshActions">The refresh callbacks owned by the system.</param>
    /// <param name="nativeResourceRegistry">The native resource registry owned by the system.</param>
    /// <returns>The installed work object.</returns>
    protected TWork InstallWork<TWork>(
        List<IUpdatableHandle> updatables,
        List<Action<SystemBase>> refreshActions,
        NativeResourceRegistry nativeResourceRegistry)
        where TWork : class, ISystemWork, new()
    {
        TWork work = new();

        VSystemBuilder builder = new(this, updatables, refreshActions, nativeResourceRegistry);
        work.Configure(builder);

        InstallQueries(builder.MainSpecOrThrow(), builder.NamedSpecs);
        InstallJobPlans(builder.JobPlans);
        return work;
    }
}

/// <summary>
/// Generic base that wires lifecycle to TWork.
/// </summary>
public abstract class VSystemBase<TWork> : VSystemBase
    where TWork : class, ISystemWork, new()
{
    protected TWork Work { get; set; } = null!;
    protected override void BuildFromWork(
        List<IUpdatableHandle> updatables,
        List<Action<SystemBase>> refreshActions,
        NativeResourceRegistry nativeResourceRegistry)
        => Work = InstallWork<TWork>(updatables, refreshActions, nativeResourceRegistry);
    protected override void OnWorkCreate(VSystemContext ctx) => Work.OnCreate(ctx);
    protected override void OnWorkStartRunning(VSystemContext ctx) => Work.OnStartRunning(ctx);
    protected override void OnWorkStopRunning(VSystemContext ctx) => Work.OnStopRunning(ctx);
    protected override void OnWorkDestroy(VSystemContext ctx) => Work.OnDestroy(ctx);
    protected override void OnWorkUpdate(VSystemContext ctx) => Work.OnUpdate(ctx);
}

/// <summary>
/// Optional quality-of-life query helpers for V Rising (Il2CppType-based ComponentType).
/// These are safe because they operate on the builder by ref (no copying).
/// </summary>
public static class VQueryDsl
{
    public static ref EntityQueryBuilder WithAllRO<T>(this ref EntityQueryBuilder b)
    {
        b.AddAll(ComponentType.ReadOnly(Il2CppType.Of<T>()));
        return ref b;
    }
    public static ref EntityQueryBuilder WithAllRW<T>(this ref EntityQueryBuilder b)
    {
        b.AddAll(ComponentType.ReadWrite(Il2CppType.Of<T>()));
        return ref b;
    }
    public static ref EntityQueryBuilder WithNone<T>(this ref EntityQueryBuilder b)
    {
        b.AddNone(Il2CppType.Of<T>());
        return ref b;
    }
    public static ref EntityQueryBuilder IncludeDisabled(this ref EntityQueryBuilder b)
    {
        b.WithOptions(EntityQueryOptions.IncludeDisabled);
        return ref b;
    }
}
/// <summary>
/// Defines a synchronous chunk iterator that Emberglass can run against an <see cref="EntityQuery"/>.
/// </summary>
public interface IChunkJob
{
    /// <summary>
    /// Executes job logic for one archetype chunk.
    /// </summary>
    /// <param name="chunk">The chunk currently being processed.</param>
    void Execute(ref ArchetypeChunk chunk);
}

/// <summary>
/// Provides deterministic, allocation-scoped chunk job execution helpers.
/// </summary>
public static class ChunkJobExtensions
{
    /// <summary>
    /// Runs a chunk job synchronously across all chunks returned by the query.
    /// </summary>
    /// <typeparam name="T">Chunk job type.</typeparam>
    /// <param name="query">Query to enumerate.</param>
    /// <param name="job">Job instance to execute and retain by-ref state from.</param>
    public static void Run<T>(this EntityQuery query, ref T job)
        where T : struct, IChunkJob
    {
        var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
        try
        {
            for (int ci = 0; ci < chunks.Length; ci++)
            {
                var chunk = chunks[ci];
                job.Execute(ref chunk);
            }
        }
        finally
        {
            chunks.Dispose();
        }
    }

    /*
    // Optional: budgeted version (flat frame time) + rolling cursor
    public static bool RunBudgeted<T>(this EntityQuery query, ref T job, int maxEntitiesThisFrame, ref int chunkCursor)
        where T : struct, IChunkJob
    {
        var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
        try
        {
            int processed = 0;
            for (int ci = chunkCursor; ci < chunks.Length; ci++)
            {
                var chunk = chunks[ci];
                job.Execute(in chunk);
                processed += chunk.Count;

                if (processed >= maxEntitiesThisFrame)
                {
                    chunkCursor = ci + 1;
                    return false; // not finished
                }
            }

            chunkCursor = 0;
            return true; // finished
        }
        finally
        {
            chunks.Dispose();
        }
    }
    */
}
