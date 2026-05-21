using Emberglass.API.Shared;
using Emberglass.CustomPrefabs;
using Unity.Entities;

namespace Emberglass.Systems;

public sealed class CustomPrefabRegistrationSystem : SystemBase
{
    bool _ran;
    int _attempts;

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
            int activeRegistrationCount = CustomPrefabRegistry.ActiveRegistrations.Count;
            CustomPrefabRegistrar registrar = new(CustomPrefabManifestStore.Default);
            int registeredCount = registrar.RegisterActiveServerPrefabs(logMissingSources: false);
            if (registeredCount > 0)
            {
                VWorld.Log.LogInfo($"[CustomPrefabs] Registered {registeredCount} active custom prefab definition(s).");
            }

            if (!ShouldDisableAfterAttempt(activeRegistrationCount, registeredCount)
                && (_attempts == 1 || _attempts % 300 == 0))
            {
                VWorld.Log.LogInfo($"[CustomPrefabs] Waiting for source prefab availability; activeRegistrations={activeRegistrationCount}, attempts={_attempts}.");
            }

            _ran = ShouldDisableAfterAttempt(activeRegistrationCount, registeredCount);
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
        => activeRegistrationCount == 0 || registeredCount > 0;
}
