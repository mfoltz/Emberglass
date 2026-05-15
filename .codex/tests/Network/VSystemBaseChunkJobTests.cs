using Emberglass.Systems;
using ProjectM.Network;
using Unity.Entities;
using Xunit;

namespace Emberglass.Tests.Network;

#pragma warning disable CS0649

/// <summary>
/// Covers the experimental VSystemBase chunk-job contract without requiring a live Unity world.
/// </summary>
[Collection("Assembly setup")]
public sealed class VSystemBaseChunkJobTests
{
    /// <summary>
    /// Ensures the chunk-job metadata path recognizes the handle shape used by simple chunk scans.
    /// </summary>
    [Fact]
    public void JobFieldInspector_RecognizesChunkJobHandleMetadata()
    {
        JobFieldMetadata Metadata = JobFieldInspector.Inspect(typeof(ServantStyleChunkJob));

        Assert.Single(Metadata.UpdatableFields);
        Assert.Contains(Metadata.UpdatableFields, Field => Field.ValueType == typeof(EntityTypeHandle));
        Assert.Empty(Metadata.NativeResourceFields);
    }

    /// <summary>
    /// Ensures lookup and handle declarations keep explicit access intent for later refresh binding.
    /// </summary>
    [Fact]
    public void JobFieldInspector_RecognizesLookupAndHandleAccessMetadata()
    {
        JobFieldMetadata Metadata = JobFieldInspector.Inspect(typeof(PresenceStyleChunkJob));

        Assert.Equal(4, Metadata.UpdatableFields.Count);
        Assert.Contains(Metadata.UpdatableFields, Field =>
            Field.ValueType == typeof(EntityStorageInfoLookup)
            && Field.AccessMode == JobFieldAccessMode.ReadWrite);
        Assert.Contains(Metadata.UpdatableFields, Field =>
            Field.ValueType == typeof(ComponentTypeHandle<User>)
            && Field.AccessMode == JobFieldAccessMode.ReadOnly);
        Assert.Contains(Metadata.UpdatableFields, Field =>
            Field.ValueType == typeof(ComponentLookup<User>)
            && Field.AccessMode == JobFieldAccessMode.ReadOnly);
        Assert.Contains(Metadata.UpdatableFields, Field =>
            Field.ValueType == typeof(ComponentLookup<User>)
            && Field.AccessMode == JobFieldAccessMode.ReadWrite);
        Assert.Empty(Metadata.NativeResourceFields);
    }

    /// <summary>
    /// Ensures the refresh seam updates declared handles before running custom refresh callbacks.
    /// </summary>
    [Fact]
    public void RefreshUpdatablesAndActions_RunsHandlesBeforeRefreshActions()
    {
        List<string> Events = new();
        List<IUpdatableHandle> Updatables = new()
        {
            new RecordingUpdatableHandle("handle:entity", Events),
            new RecordingUpdatableHandle("handle:user", Events)
        };
        List<Action<SystemBase>> RefreshActions = new()
        {
            _ => Events.Add("refresh:custom")
        };

        VSystemBase.RefreshUpdatablesAndActions(Updatables, RefreshActions, null!);

        Assert.Equal(
            new[]
            {
                "handle:entity",
                "handle:user",
                "refresh:custom"
            },
            Events);
    }

    /// <summary>
    /// Ensures planned job binding fails clearly when a job field was not declared through the builder.
    /// </summary>
    [Fact]
    public void GetRequiredUpdatableValue_ReportsMissingBuilderDeclaration()
    {
        InvalidOperationException Exception = Assert.Throws<InvalidOperationException>(() =>
            VSystemBase.GetRequiredUpdatableValue(Array.Empty<IUpdatableHandle>(), typeof(ComponentLookup<User>)));

        Assert.Contains("No updatable handle registered for 'Unity.Entities.ComponentLookup`1[ProjectM.Network.User]'", Exception.Message);
        Assert.Contains("Declare it in Configure", Exception.Message);
    }

    struct ServantStyleChunkJob : IChunkJob
    {
        public EntityTypeHandle EntityTypeHandle;

        public void Execute(ref ArchetypeChunk chunk)
        {
        }
    }

    struct PresenceStyleChunkJob : IChunkJob
    {
        public EntityStorageInfoLookup EntityStorageInfoLookup;
        public RO<ComponentTypeHandle<User>> UserTypeHandle;
        public RO<ComponentLookup<User>> UserLookup;
        public RW<ComponentLookup<User>> WritableUserLookup;

        public void Execute(ref ArchetypeChunk chunk)
        {
        }
    }

    sealed class RecordingUpdatableHandle : IUpdatableHandle
    {
        readonly string EventName;
        readonly List<string> Events;

        public RecordingUpdatableHandle(string eventName, List<string> events)
        {
            EventName = eventName;
            Events = events;
        }

        public void Update(SystemBase system)
            => Events.Add(EventName);
    }
}

#pragma warning restore CS0649
