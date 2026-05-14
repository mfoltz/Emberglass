using System.Reflection;
using Unity.Collections;

namespace Emberglass.Systems;

/// <summary>
/// Describes a native resource that can be managed by a system.
/// </summary>
public interface INativeResource : IDisposable
{
    /// <summary>
    /// Gets or sets whether the resource should be cleared on each update.
    /// </summary>
    bool ClearEachUpdate { get; set; }

    /// <summary>
    /// Clears the resource contents without releasing the allocation.
    /// </summary>
    void Clear();

    /// <summary>
    /// Ensures the resource can hold the requested number of elements.
    /// </summary>
    /// <param name="needed">The number of elements that must fit in the resource.</param>
    void EnsureCapacity(int needed);
}

/// <summary>
/// Owns a <see cref="NativeParallelHashSet{T}"/> allocated with <see cref="Allocator.Persistent"/>.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class HashSetResource<T> : INativeResource
    where T : unmanaged, IEquatable<T>
{
    const int DEFAULT_CAPACITY = 16;
    const int MIN_CAPACITY = 1;

    /// <summary>
    /// Initializes a new instance with the provided capacity.
    /// </summary>
    /// <param name="initialCapacity">The initial capacity to allocate.</param>
    public HashSetResource(int initialCapacity = DEFAULT_CAPACITY)
    {
        int capacity = NativeResourceMath.NextPow2(Math.Max(MIN_CAPACITY, initialCapacity));
        Value = new(capacity, Allocator.Persistent);
        ClearEachUpdate = true;
    }

    /// <summary>
    /// Gets the underlying hash set.
    /// </summary>
    public NativeParallelHashSet<T> Value { get; set; }

    /// <inheritdoc />
    public bool ClearEachUpdate { get; set; }

    /// <inheritdoc />
    public void Clear()
    {
        if (Value.IsCreated)
        {
            Value.Clear();
        }
    }

    /// <inheritdoc />
    public void EnsureCapacity(int needed)
    {
        int capacity = NativeResourceMath.NextPow2(Math.Max(MIN_CAPACITY, needed));
        if (!Value.IsCreated)
        {
            Value = new NativeParallelHashSet<T>(capacity, Allocator.Persistent);
            return;
        }

        if (Value.Capacity < capacity)
        {
            var value = Value;
            value.Capacity = capacity;
            Value = value;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Value.IsCreated)
        {
            Value.Dispose();
        }
    }
}

/// <summary>
/// Owns a <see cref="NativeParallelMultiHashMap{TKey, TValue}"/> allocated with <see cref="Allocator.Persistent"/>.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed class MultiHashMapResource<TKey, TValue> : INativeResource
    where TKey : unmanaged, IEquatable<TKey>
    where TValue : unmanaged
{
    const int DEFAULT_CAPACITY = 16;
    const int MIN_CAPACITY = 1;

    /// <summary>
    /// Initializes a new instance with the provided capacity.
    /// </summary>
    /// <param name="initialCapacity">The initial capacity to allocate.</param>
    public MultiHashMapResource(int initialCapacity = DEFAULT_CAPACITY)
    {
        int capacity = NativeResourceMath.NextPow2(Math.Max(MIN_CAPACITY, initialCapacity));
        Value = new NativeParallelMultiHashMap<TKey, TValue>(capacity, Allocator.Persistent);
        ClearEachUpdate = true;
    }

    /// <summary>
    /// Gets the underlying multi hash map.
    /// </summary>
    public NativeParallelMultiHashMap<TKey, TValue> Value { get; private set; }

    /// <inheritdoc />
    public bool ClearEachUpdate { get; set; }

    /// <inheritdoc />
    public void Clear()
    {
        if (Value.IsCreated)
        {
            Value.Clear();
        }
    }

    /// <inheritdoc />
    public void EnsureCapacity(int needed)
    {
        int capacity = NativeResourceMath.NextPow2(Math.Max(MIN_CAPACITY, needed));
        if (!Value.IsCreated)
        {
            Value = new NativeParallelMultiHashMap<TKey, TValue>(capacity, Allocator.Persistent);
            return;
        }

        if (Value.Capacity < capacity)
        {
            var value = Value;
            value.Capacity = capacity;
            Value = value;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Value.IsCreated)
        {
            Value.Dispose();
        }
    }
}

/// <summary>
/// Utility helpers for native resource sizing.
/// </summary>
static class NativeResourceMath
{
    /// <summary>
    /// Rounds the provided value up to the next power of two.
    /// </summary>
    /// <param name="value">The value to round up.</param>
    /// <returns>The next power of two.</returns>
    public static int NextPow2(int value)
    {
        if (value <= 1)
        {
            return 1;
        }

        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }
}

/// <summary>
/// Tracks native resources that are owned by a system.
/// </summary>
public sealed class NativeResourceRegistry
{
    readonly Dictionary<FieldInfo, INativeResource> fieldResources = [];

    /// <summary>
    /// Registers native resources for the provided job fields.
    /// </summary>
    /// <param name="fields">The native resource fields to register.</param>
    public void Register(IReadOnlyList<JobFieldInfo> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            Register(fields[i]);
        }
    }

    /// <summary>
    /// Clears resources that are configured to be cleared each update.
    /// </summary>
    public void ClearEachUpdateResources()
    {
        foreach (var resource in fieldResources.Values)
        {
            if (resource.ClearEachUpdate)
            {
                resource.Clear();
            }
        }
    }

    /// <summary>
    /// Ensures capacity for registered resources tied to the provided job fields.
    /// </summary>
    /// <param name="fields">The job fields describing the resources.</param>
    /// <param name="needed">The required capacity.</param>
    public void EnsureCapacity(IReadOnlyList<JobFieldInfo> fields, int needed)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            var fieldInfo = fields[i];
            if (!fieldResources.TryGetValue(fieldInfo.Field, out var resource))
            {
                throw new InvalidOperationException($"No native resource registered for field '{fieldInfo.Field.Name}'.");
            }

            resource.EnsureCapacity(needed);
        }
    }

    /// <summary>
    /// Gets the resource view for a job field and requested value type.
    /// </summary>
    /// <param name="fieldInfo">The job field metadata.</param>
    /// <param name="requestedType">The requested value type.</param>
    /// <returns>The resource value to assign to the job field.</returns>
    public object GetResourceValue(JobFieldInfo fieldInfo, Type requestedType)
    {
        if (!fieldResources.TryGetValue(fieldInfo.Field, out var resource))
        {
            throw new InvalidOperationException($"No native resource registered for field '{fieldInfo.Field.Name}'.");
        }

        object container = GetResourceContainer(resource);
        var containerType = container.GetType();
        if (requestedType == containerType)
        {
            return container;
        }

        var readOnlyType = containerType.GetNestedType("ReadOnly");
        if (requestedType == readOnlyType)
        {
            return InvokeContainerView(container, "AsReadOnly");
        }

        var parallelWriterType = containerType.GetNestedType("ParallelWriter");
        if (requestedType == parallelWriterType)
        {
            return InvokeContainerView(container, "AsParallelWriter");
        }

        throw new InvalidOperationException($"Unsupported resource view type '{requestedType}'.");
    }

    /// <summary>
    /// Disposes all registered resources.
    /// </summary>
    public void DisposeResources()
    {
        foreach (var resource in fieldResources.Values)
        {
            resource.Dispose();
        }

        fieldResources.Clear();
    }

    void Register(JobFieldInfo fieldInfo)
    {
        if (fieldResources.ContainsKey(fieldInfo.Field))
        {
            return;
        }

        var resourceType = fieldInfo.ResourceType
            ?? throw new InvalidOperationException("Native resource fields must include a resource type.");
        var genericDefinition = resourceType.GetGenericTypeDefinition();
        if (genericDefinition == typeof(NativeParallelHashSet<>))
        {
            var elementType = resourceType.GetGenericArguments()[0];
            var resource = CreateResource(typeof(HashSetResource<>), elementType);
            fieldResources.Add(fieldInfo.Field, resource);
            return;
        }

        if (genericDefinition == typeof(NativeParallelMultiHashMap<,>))
        {
            var genericArguments = resourceType.GetGenericArguments();
            var resource = CreateResource(typeof(MultiHashMapResource<,>), genericArguments);
            fieldResources.Add(fieldInfo.Field, resource);
            return;
        }

        throw new InvalidOperationException($"Unsupported native resource type '{resourceType}'.");
    }

    static INativeResource CreateResource(Type resourceDefinition, params Type[] genericArguments)
    {
        var resourceType = resourceDefinition.MakeGenericType(genericArguments);
        return (INativeResource)Activator.CreateInstance(resourceType)
            ?? throw new InvalidOperationException($"Failed to create native resource '{resourceType}'.");
    }

    /// <summary>
    /// Gets the underlying native container from a resource instance.
    /// </summary>
    /// <param name="resource">The native resource instance.</param>
    /// <returns>The underlying native container.</returns>
    static object GetResourceContainer(INativeResource resource)
    {
        var resourceType = resource.GetType();
        var valueProperty = resourceType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Native resource '{resourceType}' is missing a Value property.");
        return valueProperty.GetValue(resource)
            ?? throw new InvalidOperationException($"Native resource '{resourceType}' did not provide a Value instance.");
    }

    /// <summary>
    /// Invokes a view creation method on a native container.
    /// </summary>
    /// <param name="container">The native container instance.</param>
    /// <param name="methodName">The view method name.</param>
    /// <returns>The view instance.</returns>
    static object InvokeContainerView(object container, string methodName)
    {
        var method = container.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Native container is missing '{methodName}'.");
        return method.Invoke(container, Array.Empty<object>())
            ?? throw new InvalidOperationException($"Native container view '{methodName}' returned null.");
    }
}
