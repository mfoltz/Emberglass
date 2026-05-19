using Stunlock.Core;
using System.Security.Cryptography;
using System.Text;
using Il2CppGuid = Il2CppSystem.Guid;

namespace Emberglass.CustomPrefabs;

internal readonly record struct CustomPrefabIds(int GeneratedPrefabGuid, string GeneratedAssetGuid)
{
    public AssetGuid ToAssetGuid()
    {
        Guid guid = Guid.ParseExact(GeneratedAssetGuid, "N");
        return AssetGuid.FromGuid(new Il2CppGuid(guid.ToByteArray()));
    }

    public static CustomPrefabIds Create(string providerId, int sourcePrefabGuid, string generatedAssetName)
    {
        string seed = $"{providerId}|{sourcePrefabGuid}|{generatedAssetName}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

        Guid assetGuid = new(hashBytes[..16]);
        int prefabGuid = BitConverter.ToInt32(hashBytes, 16);
        if (prefabGuid == 0 || prefabGuid == sourcePrefabGuid)
        {
            prefabGuid = BitConverter.ToInt32(hashBytes, 20);
        }

        if (prefabGuid == 0 || prefabGuid == sourcePrefabGuid)
        {
            prefabGuid = sourcePrefabGuid ^ unchecked((int)0x5EEDC0DE);
        }

        return new(prefabGuid, assetGuid.ToString("N"));
    }
}
