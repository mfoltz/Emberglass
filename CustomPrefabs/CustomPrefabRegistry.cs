namespace Emberglass.CustomPrefabs;

internal static class CustomPrefabRegistry
{
    static readonly object Gate = new();
    static readonly Dictionary<int, CustomPrefabRegistration> RegistrationsByGeneratedPrefabGuid = [];

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

        lock (Gate)
        {
            RegistrationsByGeneratedPrefabGuid[registration.GeneratedPrefabGuid] = registration;
        }

        return registration;
    }

    public static void ClearForTesting()
    {
        lock (Gate)
        {
            RegistrationsByGeneratedPrefabGuid.Clear();
        }
    }
}
