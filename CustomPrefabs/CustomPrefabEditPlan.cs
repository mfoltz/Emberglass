using Emberglass.API.Shared;
using Unity.Entities;

namespace Emberglass.CustomPrefabs;

internal sealed class CustomPrefabEditPlan
{
    public static readonly CustomPrefabEditPlan Empty = new("none", []);

    readonly CustomPrefabEdit[] _edits;

    CustomPrefabEditPlan(string name, IReadOnlyList<CustomPrefabEdit> edits)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "unnamed" : name;
        _edits = edits.ToArray();
    }

    public string Name { get; }
    public int Count => _edits.Length;
    public bool IsEmpty => _edits.Length == 0;

    public static CustomPrefabEditPlan Create(string name, Action<CustomPrefabEditBuilder> configure)
    {
        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        CustomPrefabEditBuilder builder = new();
        configure(builder);
        return new(name, builder.Build());
    }

    public CustomPrefabEditReceipt Apply(CustomPrefabRegistration registration, Entity rootEntity)
    {
        CustomPrefabEditContext context = new(registration, rootEntity);
        foreach (CustomPrefabEdit edit in _edits)
        {
            edit.Apply(context);
        }

        return context.ToReceipt(Name);
    }
}

internal sealed class CustomPrefabEditBuilder
{
    readonly List<CustomPrefabEdit> _edits = [];

    public void Remove<T>(string description = null) where T : struct
        => Add(typeof(T), description, Context => Context.Remove<T>());

    public void RemoveBuffer<T>(string description = null) where T : struct
        => Add(typeof(T), description, Context => Context.Remove<T>());

    public void Edit<T>(VExtensions.ActionRefHandler<T> edit, string description = null) where T : struct
    {
        if (edit == null)
        {
            throw new ArgumentNullException(nameof(edit));
        }

        Add(typeof(T), description, Context => Context.Edit(edit));
    }

    public void EditBuffer<T>(Action<DynamicBuffer<T>> edit, string description = null) where T : struct
    {
        if (edit == null)
        {
            throw new ArgumentNullException(nameof(edit));
        }

        Add(typeof(T), description, Context => Context.EditBuffer(edit));
    }

    internal IReadOnlyList<CustomPrefabEdit> Build()
        => _edits;

    void Add(Type componentType, string description, Action<CustomPrefabEditContext> apply)
        => _edits.Add(new(
            componentType.Name,
            string.IsNullOrWhiteSpace(description) ? componentType.Name : description,
            apply));
}

internal readonly record struct CustomPrefabEditReceipt(
    string PlanName,
    int AppliedCount,
    int SkippedCount,
    IReadOnlyList<string> Messages)
{
    public int TotalCount => AppliedCount + SkippedCount;
}

internal sealed class CustomPrefabEditContext
{
    readonly List<string> _messages = [];

    public CustomPrefabEditContext(CustomPrefabRegistration registration, Entity rootEntity)
    {
        Registration = registration;
        RootEntity = rootEntity;
    }

    public CustomPrefabRegistration Registration { get; }
    public Entity RootEntity { get; }
    public int AppliedCount { get; private set; }
    public int SkippedCount { get; private set; }

    internal void Remove<T>() where T : struct
    {
        string componentName = typeof(T).Name;
        if (!RootEntity.Has<T>())
        {
            Skip(componentName, "missing");
            return;
        }

        RootEntity.Remove<T>();
        Apply(componentName, "removed");
    }

    internal void Edit<T>(VExtensions.ActionRefHandler<T> edit) where T : struct
    {
        string componentName = typeof(T).Name;
        if (!RootEntity.Has<T>())
        {
            Skip(componentName, "missing");
            return;
        }

        RootEntity.With(edit);
        Apply(componentName, "edited");
    }

    internal void EditBuffer<T>(Action<DynamicBuffer<T>> edit) where T : struct
    {
        string componentName = typeof(T).Name;
        if (!RootEntity.TryGetBuffer(out DynamicBuffer<T> buffer))
        {
            Skip(componentName, "missing");
            return;
        }

        edit(buffer);
        Apply(componentName, "edited");
    }

    internal CustomPrefabEditReceipt ToReceipt(string planName)
        => new(planName, AppliedCount, SkippedCount, _messages.ToArray());

    void Apply(string componentName, string action)
    {
        AppliedCount++;
        _messages.Add($"{componentName}:{action}");
    }

    void Skip(string componentName, string reason)
    {
        SkippedCount++;
        _messages.Add($"{componentName}:skipped:{reason}");
    }
}

internal readonly record struct CustomPrefabEdit(
    string ComponentName,
    string Description,
    Action<CustomPrefabEditContext> Apply);
