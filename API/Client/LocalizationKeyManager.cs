using Stunlock.Core;
using Stunlock.Localization;
using System.Security.Cryptography;
using System.Text;
using Guid = Il2CppSystem.Guid;

namespace Emberglass.API.Client;
public static class LocalizationKeyManager
{
    public static IReadOnlyDictionary<string, LocalizationKey> CategoryHeaders => _categoryHeaders;
    static readonly Dictionary<string, LocalizationKey> _categoryHeaders = [];
    public static IReadOnlyDictionary<AssetGuid, string> AssetGuids => _assetGuids;
    static readonly Dictionary<AssetGuid, string> _assetGuids = [];
    public static void LocalizeCategoryHeader(string categoryHeader)
    {
        if (!_categoryHeaders.TryGetValue(categoryHeader, out var localizationKey))
        {
            localizationKey = GetLocalizationKey(categoryHeader);
            _categoryHeaders[categoryHeader] = localizationKey;
        }
    }
    public static void LocalizeText()
    {
        foreach (var keyValuePair in AssetGuids)
        {
            AssetGuid assetGuid = keyValuePair.Key;
            string localizedString = keyValuePair.Value;

            Localization._LocalizedStrings.TryAdd(assetGuid, localizedString);
        }
    }
    static AssetGuid GetAssetGuid(string text)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text));

        Guid uniqueGuid = new(hashBytes[..16]);
        return AssetGuid.FromGuid(uniqueGuid);
    }
    public static LocalizationKey GetLocalizationKey(string value)
    {
        AssetGuid assetGuid = GetAssetGuid(value);
        _assetGuids.TryAdd(assetGuid, value);

        return new(assetGuid);
    }
}
