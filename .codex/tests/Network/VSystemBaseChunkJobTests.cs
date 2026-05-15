using Emberglass.Systems;
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

    struct ServantStyleChunkJob : IChunkJob
    {
        public EntityTypeHandle EntityTypeHandle;

        public void Execute(ref ArchetypeChunk chunk)
        {
        }
    }
}

#pragma warning restore CS0649
