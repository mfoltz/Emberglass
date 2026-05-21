namespace Emberglass.CustomPrefabs;

internal static class CustomPrefabRegistry
{
    static readonly object Gate = new();
    static readonly Dictionary<int, CustomPrefabRegistration> RegistrationsByGeneratedPrefabGuid = [];
    static readonly Dictionary<int, CustomPrefabEditPlan> EditPlansByGeneratedPrefabGuid = [];

    public static IReadOnlyList<CustomPrefabRegistration> ActiveRegistrations
    {
        get
        {
            lock (Gate)
            {
                return RegistrationsByGeneratedPrefabGuid.Values.ToArray();
            }
        }
    }

    public static CustomPrefabRegistration Register(CustomPrefabDefinition definition)
    {
        CustomPrefabRegistration registration = CustomPrefabRegistration.Create(definition);
        CustomPrefabEditPlan editPlan = definition.EditPlan ?? CustomPrefabEditPlan.Empty;

        lock (Gate)
        {
            RegistrationsByGeneratedPrefabGuid[registration.GeneratedPrefabGuid] = registration;
            if (editPlan.IsEmpty)
            {
                EditPlansByGeneratedPrefabGuid.Remove(registration.GeneratedPrefabGuid);
            }
            else
            {
                EditPlansByGeneratedPrefabGuid[registration.GeneratedPrefabGuid] = editPlan;
            }
        }

        return registration;
    }

    public static bool TryGetEditPlan(int generatedPrefabGuid, out CustomPrefabEditPlan editPlan)
    {
        lock (Gate)
        {
            return EditPlansByGeneratedPrefabGuid.TryGetValue(generatedPrefabGuid, out editPlan);
        }
    }

    public static void ClearForTesting()
    {
        lock (Gate)
        {
            RegistrationsByGeneratedPrefabGuid.Clear();
            EditPlansByGeneratedPrefabGuid.Clear();
        }
    }
}
