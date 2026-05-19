using Emberglass.API.Shared;
using Emberglass.CustomPrefabs;
using Unity.Entities;

namespace Emberglass.Systems;

public sealed class CustomPrefabRegistrationSystem : SystemBase
{
    bool _ran;

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
            CustomPrefabRegistrar registrar = new(CustomPrefabManifestStore.Default);
            int registeredCount = registrar.RegisterActiveServerPrefabs();
            if (registeredCount > 0)
            {
                VWorld.Log.LogInfo($"[CustomPrefabs] Registered {registeredCount} active custom prefab definition(s).");
            }
        }
        catch (Exception ex)
        {
            VWorld.Log.LogError($"[CustomPrefabs] Registration failed: {ex}");
        }
        finally
        {
            _ran = true;
            Enabled = false;
        }
    }
}
