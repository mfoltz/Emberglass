using BepInEx.Unity.IL2CPP.Hook;
using HarmonyLib;
using System.Reflection;

namespace Emberglass.Utilities;
public static class NativeDetour
{
    /*
    public static INativeDetour Detour<T>(string typeName, string methodName, T to, out T original) where T : Delegate
    {
        return Detour(Type.GetType(typeName), methodName, to, out original);
    }
    public static INativeDetour Detour<T>(Type type, string methodName, T to, out T original) where T : Delegate
    {
        var method = type.GetMethod(methodName, AccessTools.all);
        return Detour(method, to, out original);
    }
    public static INativeDetour Detour<T>(MethodInfo method, T to, out T original) where T : Delegate
    {
        var address = Il2CppMethodResolver.ResolveFromMethodInfo(method);
        return INativeDetour.CreateAndApply(address, to, out original);
    }
    public static INativeDetour Detour<T>(IntPtr intPtr, T to, out T original) where T : Delegate
    {
        return INativeDetour.CreateAndApply(intPtr, to, out original);
    }
    */

    public static INativeDetour Create<T>(Type type, string innerTypeName, string methodName, T to, out T original) where T : Delegate
    {
        return Create(GetInnerType(type, innerTypeName, methodName), methodName, to, out original);
    }
    public static INativeDetour Create<T>(Type type, string methodName, T to, out T original) where T : Delegate
    {
        var method = type.GetMethod(methodName, AccessTools.all);
        if (method == null)
        {
            throw new ArgumentException(
                $"Method '{methodName}' not found on type '{type.FullName}'.",
                nameof(methodName));
        }

        return Create(method, to, out original);
    }

    /*
    public static INativeDetour CreateBySignature<T>(
        Type type,
        Func<Type, bool> nestedTypePredicate,
        Func<MethodInfo, bool> methodPredicate,
        T to,
        out T original) where T : Delegate
    {
        var nestedType = type.GetNestedTypes()
            .FirstOrDefault(nestedTypePredicate)
            ?? throw new ArgumentException("Nested type not found", nameof(nestedTypePredicate));

        var method = nestedType
            .GetMethods(AccessTools.all)
            .FirstOrDefault(methodPredicate)
            ?? throw new ArgumentException("Method not found", nameof(methodPredicate));

        return Create(method, to, out original);
    }
    */

    static INativeDetour Create<T>(MethodInfo method, T to, out T original) where T : Delegate
    {
        var address = Il2CppMethodResolver.ResolveFromMethodInfo(method);
        return INativeDetour.CreateAndApply(address, to, out original);
    }

    static Type GetInnerType(Type type, string innerTypeName, string methodName)
    {
        var candidates = type
            .GetNestedTypes()
            .Where(x => x.Name.Contains(innerTypeName) && x.GetMethod(methodName, AccessTools.all) != null)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new ArgumentException(
                $"Nested type containing method '{methodName}' not found for substring '{innerTypeName}'",
                nameof(innerTypeName));
        }

        return candidates[0];
    }
}
