using Emberglass.API.Shared;
using Emberglass.CustomPrefabs;
using Unity.Entities;

namespace Emberglass.Systems;

public sealed class CustomPrefabRegistrationSystem : SystemBase
{
    bool _ran;
    int _attempts;
    readonly HashSet<int> _registeredGeneratedPrefabGuids = [];

    public override void OnCreate()
    {
        Enabled = true;
    }

    public override void OnUpdate()
    {
        if (_ran)
        {
            Enabled = false;
            return;
        }

        try
        {
            _attempts++;
            IReadOnlyList<CustomPrefabRegistration> activeRegistrations = CustomPrefabRegistry.ActiveRegistrations;
            int activeRegistrationCount = activeRegistrations.Count;
            CustomPrefabRegistrar registrar = new(CustomPrefabManifestStore.Default);
            int registeredCount = 0;

            foreach (CustomPrefabRegistration registration in activeRegistrations)
            {
                if (_registeredGeneratedPrefabGuids.Contains(registration.GeneratedPrefabGuid))
                {
                    continue;
                }

                if (registrar.TryRegister(
                    registration,
                    recordManifest: true,
                    logMissingSources: false,
                    out _))
                {
                    _registeredGeneratedPrefabGuids.Add(registration.GeneratedPrefabGuid);
                    registeredCount++;
                }
            }

            int completedRegistrationCount = activeRegistrations.Count(
                registration => _registeredGeneratedPrefabGuids.Contains(registration.GeneratedPrefabGuid));

            if (registeredCount > 0)
            {
                VWorld.Log.LogInfo($"[CustomPrefabs] Registered {registeredCount} active custom prefab definition(s).");
            }

            if (!ShouldDisableAfterAttempt(activeRegistrationCount, completedRegistrationCount)
                && (_attempts == 1 || _attempts % 300 == 0))
            {
                VWorld.Log.LogInfo($"[CustomPrefabs] Waiting for source prefab availability; activeRegistrations={activeRegistrationCount}, attempts={_attempts}.");
            }

            _ran = ShouldDisableAfterAttempt(activeRegistrationCount, completedRegistrationCount);
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"[CustomPrefabs] Registration failed: {ex}");
            _ran = true;
        }
        finally
        {
            Enabled = !_ran;
        }
    }

    internal static bool ShouldDisableAfterAttempt(int activeRegistrationCount, int registeredCount)
        => activeRegistrationCount == 0 || registeredCount >= activeRegistrationCount;
}
