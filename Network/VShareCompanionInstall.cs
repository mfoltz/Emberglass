namespace Emberglass.Network;

// Tracks the expected companion slots for one verified mod install on one connection.
internal sealed class VShareCompanionInstall(
    int companionCount,
    ulong sourcePlatformId,
    long clientSessionGeneration,
    DateTime startedAt,
    TimeSpan idleTimeout)
{
    readonly Dictionary<int, Guid> receiving = [];
    readonly HashSet<int> resolved = [];
    readonly HashSet<Guid> transferIds = [];
    readonly List<VShareManagedConfigWrite> appliedConfigs = [];
    DateTime expiresAt = startedAt.Add(idleTimeout);

    public bool Failed { get; private set; }
    public bool IsComplete => !Failed && resolved.Count == companionCount;
    public IReadOnlyList<VShareManagedConfigWrite> AppliedConfigs => appliedConfigs;
    public int PreservedConfigCount { get; private set; }

    public bool IsCurrent(long generation, DateTime now)
        => generation == clientSessionGeneration && now < expiresAt;

    public bool TryKeepWaiting(ulong source, long generation, DateTime now)
    {
        if (Failed || source != sourcePlatformId || !IsCurrent(generation, now)
            || receiving.Count + resolved.Count >= companionCount)
        {
            return false;
        }

        expiresAt = now.Add(idleTimeout);
        return true;
    }

    public bool TryBegin(int index, Guid transferId, ulong source, long generation, DateTime now)
    {
        if (Failed || source != sourcePlatformId || !IsCurrent(generation, now)
            || index < 0 || index >= companionCount || resolved.Contains(index)
            || receiving.ContainsKey(index) || transferId == Guid.Empty || !transferIds.Add(transferId))
        {
            return false;
        }

        receiving.Add(index, transferId);
        expiresAt = now.Add(idleTimeout);
        return true;
    }

    public bool CanApply(int index, Guid transferId, ulong source, long generation, DateTime now)
        => !Failed && source == sourcePlatformId && IsCurrent(generation, now)
            && receiving.TryGetValue(index, out Guid activeId) && activeId == transferId;

    public void RecordChunk(int index, Guid transferId, ulong source, long generation, DateTime now)
    {
        if (CanApply(index, transferId, source, generation, now))
        {
            expiresAt = now.Add(idleTimeout);
        }
    }

    public void Finish(int index, Guid transferId, VShareConfigApplyResult result)
        => Finish(index, transferId, IsResolved(result.Status), result.ManagedWrite,
            result.Status is VShareConfigApplyStatus.SkippedUnmanagedExisting or VShareConfigApplyStatus.SkippedLocallyEdited);

    public void Finish(int index, Guid transferId, bool resolved)
        => Finish(index, transferId, resolved, null, false);

    void Finish(int index, Guid transferId, bool resolved, VShareManagedConfigWrite? write, bool preserved)
    {
        if (!receiving.TryGetValue(index, out Guid activeId) || activeId != transferId)
        {
            return;
        }

        receiving.Remove(index);
        if (resolved)
        {
            this.resolved.Add(index);
            if (write is { } applied)
            {
                appliedConfigs.Add(applied);
            }

            if (preserved)
            {
                PreservedConfigCount++;
            }
        }
        else
        {
            Failed = true;
        }
    }

    public static bool IsResolved(VShareConfigApplyStatus status)
        => status is VShareConfigApplyStatus.AppliedNewFile
            or VShareConfigApplyStatus.AppliedManagedOverwrite
            or VShareConfigApplyStatus.SkippedUnmanagedExisting
            or VShareConfigApplyStatus.SkippedLocallyEdited;
}
