using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BepInEx;
using Emberglass.API.Shared;
using static Emberglass.Network.Registry;

namespace Emberglass.Network;

internal enum VShareConfigApplyStatus
{
    AppliedNewFile,
    AppliedManagedOverwrite,
    SkippedUnmanagedExisting,
    SkippedLocallyEdited,
    RejectedInvalidPath,
    WriteFailed
}

internal readonly record struct VShareConfigApplyResult(
    bool Success,
    VShareConfigApplyStatus Status,
    string Message,
    string DestinationPath,
    VShareManagedConfigWrite? ManagedWrite = null);

internal readonly record struct VShareManagedConfigWrite(
    string PluginKey, string Source, string Target, string Sha256, DateTimeOffset AppliedUtc);

internal sealed record VShareHotloadConfigSnapshot(
    VShareManagedConfigWrite Write, string ConfigRoot, string PluginGuid, string[] ContentLines);

internal enum VShareConfigWriteStep
{
    BeforeConfigPublish,
    BeforeManifestPublish,
    BeforeRollback
}

internal readonly record struct VShareResolvedConfigCompanion(
    string PluginKey,
    string DisplayName,
    string Source,
    string Target);

internal static class VShareConfigCompanion
{
    const string MANAGED_CONFIGS_FILE_NAME = "VShareManagedConfigs.json";
    const string SERVER_CONFIGS_DIRECTORY = "ServerConfigs";
    const string TRANSACTION_PREFIX = ".vshare-config-";
    const int MAX_HOTLOAD_CONFIG_BYTES = 1024 * 1024;
    static readonly char[] _pathSeparators = ['/', '\\'];
    static readonly char[] _invalidNameChars = ['<', '>', ':', '"', '|', '?', '*', '~'];
    static readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string ServerConfigsPath => Path.Combine(Paths.ConfigPath, SERVER_CONFIGS_DIRECTORY);

    public static string ManagedConfigsManifestPath =>
        Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME, MANAGED_CONFIGS_FILE_NAME);

    public static void EnsureDirectories()
    {
        try
        {
            EnsureSafeDirectory(Paths.ConfigPath, ServerConfigsPath);
            EnsureSafeDirectory(Paths.ConfigPath, Path.GetDirectoryName(ManagedConfigsManifestPath)!);
        }
        catch (Exception ex) when (IsFileAccessError(ex))
        {
            VWorld.Log?.LogWarning($"[VShare] Companion config directories unavailable: {ex.Message}");
        }
    }

    public static bool TryResolveServerConfigSource(
        string pluginKey,
        string source,
        out string sourcePath,
        out string errorMessage)
        => TryResolveServerConfigSource(Paths.ConfigPath, pluginKey, source, out sourcePath, out errorMessage);

    public static bool TryValidateClientConfigTargetSyntax(string target, out string errorMessage)
        => TryNormalizeRelativeFile(target, isTarget: true, out _, out errorMessage);

    public static bool TryGetServerConfigBytes(
        string pluginKey,
        string source,
        out byte[] bytes,
        out string sourcePath,
        out string errorMessage)
    {
        bytes = [];
        if (!TryResolveServerConfigSource(pluginKey, source, out sourcePath, out errorMessage))
        {
            return false;
        }

        try
        {
            bytes = File.ReadAllBytes(sourcePath);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errorMessage = $"Unable to read staged config '{source}': {ex.Message}";
            return false;
        }
    }

    public static VShareConfigApplyResult ApplyClientConfig(
        byte[] bytes,
        string pluginKey,
        string displayName,
        string source,
        string target,
        DateTimeOffset appliedUtc)
        => ApplyClientConfig(
            bytes,
            Paths.ConfigPath,
            ManagedConfigsManifestPath,
            pluginKey,
            displayName,
            source,
            target,
            appliedUtc);

    static VShareConfigApplyResult ApplyClientConfig(
        byte[] bytes,
        string configRoot,
        string manifestPath,
        string pluginKey,
        string displayName,
        string source,
        string target,
        DateTimeOffset appliedUtc,
        Action<VShareConfigWriteStep> checkpoint = null)
    {
        bytes ??= [];
        displayName ??= string.Empty;

        if (!IsPluginKeyValid(pluginKey))
        {
            return new(false, VShareConfigApplyStatus.RejectedInvalidPath, "Invalid companion config plugin key.", string.Empty);
        }

        if (!TryNormalizeRelativeFile(source, isTarget: false, out source, out string pathError)
            || !TryNormalizeRelativeFile(target, isTarget: true, out target, out pathError)
            || !TryResolveFile(configRoot, target, mustExist: false, out string destinationPath, out pathError))
        {
            return new(false, VShareConfigApplyStatus.RejectedInvalidPath, pathError, string.Empty);
        }

        string configTemp = null;
        string manifestTemp = null;
        string backup = null;
        string previousSha = null;
        string nextSha = null;
        bool published = false;
        try
        {
            ManifestSnapshot snapshot = LoadManifest(configRoot, manifestPath);
            if (ProbePath(configRoot, destinationPath, directory: false))
            {
                ManagedConfigEntry entry = snapshot.Manifest.FindEntry(pluginKey, source, target);
                if (entry is null)
                {
                    return SkipExisting(target, destinationPath, managed: false);
                }

                previousSha = HashFile(destinationPath);
                if (!HashesMatch(previousSha, entry.Sha256))
                {
                    return SkipExisting(target, destinationPath, managed: true);
                }
            }

            nextSha = HashBytes(bytes);
            snapshot.Manifest.Upsert(new ManagedConfigEntry
            {
                PluginKey = pluginKey,
                DisplayName = displayName,
                Source = source,
                Target = target,
                Sha256 = nextSha,
                AppliedUtc = appliedUtc
            });

            configTemp = StageFile(configRoot, destinationPath, bytes);
            manifestTemp = StageManifest(configRoot, manifestPath, snapshot.Manifest);
            checkpoint?.Invoke(VShareConfigWriteStep.BeforeConfigPublish);
            RequireFileState(configRoot, manifestPath, snapshot.Sha256);
            bool stillExists = ProbePath(configRoot, destinationPath, directory: false);
            if (stillExists && (previousSha is null || !HashesMatch(HashFile(destinationPath), previousSha)))
            {
                return SkipExisting(target, destinationPath, managed: previousSha is not null);
            }

            if (stillExists != (previousSha is not null))
            {
                throw new IOException("The managed config disappeared before publication.");
            }

            if (stillExists)
            {
                backup = NewTemporaryPath(destinationPath, ".bak");
                RequireFileState(configRoot, backup, expectedSha: null);
            }

            PublishFile(configRoot, configTemp, destinationPath, replace: stillExists, backup: backup);
            published = true;
            checkpoint?.Invoke(VShareConfigWriteStep.BeforeManifestPublish);
            RequireFileState(configRoot, destinationPath, nextSha);
            PublishManifest(configRoot, manifestTemp, manifestPath, snapshot.Sha256);
            published = false;
            DeleteTempFile(configRoot, backup);

            return new(
                true,
                stillExists ? VShareConfigApplyStatus.AppliedManagedOverwrite : VShareConfigApplyStatus.AppliedNewFile,
                $"Applied VShare config {target}.", destinationPath,
                new VShareManagedConfigWrite(pluginKey, source, target, nextSha, appliedUtc));
        }
        catch (Exception ex) when (IsFileAccessError(ex))
        {
            string error = $"Failed to write VShare config {target}: {ex.Message}";
            if (published)
            {
                try
                {
                    checkpoint?.Invoke(VShareConfigWriteStep.BeforeRollback);
                    RequireFileState(configRoot, destinationPath, nextSha);
                    if (backup is null)
                    {
                        File.Delete(destinationPath);
                    }
                    else
                    {
                        RequireFileState(configRoot, backup, previousSha);
                        PublishFile(configRoot, backup, destinationPath, replace: true);
                    }
                }
                catch (Exception rollbackError) when (IsFileAccessError(rollbackError))
                {
                    error += $" Rollback did not complete: {rollbackError.Message}";
                    if (backup is not null)
                    {
                        error += $" Check the recovery backup at '{backup}'.";
                    }
                }
            }

            return new(false, VShareConfigApplyStatus.WriteFailed, error, destinationPath);
        }
        finally
        {
            DeleteTempFile(configRoot, configTemp);
            DeleteTempFile(configRoot, manifestTemp);
        }
    }

    static VShareConfigApplyResult SkipExisting(string target, string path, bool managed)
        => new(false,
            managed ? VShareConfigApplyStatus.SkippedLocallyEdited : VShareConfigApplyStatus.SkippedUnmanagedExisting,
            managed ? $"Skipping {target}: VShare-managed config was locally edited."
                : $"Skipping {target}: local config already exists and is not VShare-managed.",
            path);

    internal static VShareHotloadConfigSnapshot CaptureBeforeHotload(
        string configRoot, string manifestPath, string pluginGuid, IReadOnlyList<VShareManagedConfigWrite> writes)
    {
        if (writes is null || writes.Count == 0 || string.IsNullOrWhiteSpace(pluginGuid))
        {
            return null;
        }

        string target = $"{pluginGuid}.cfg";
        VShareManagedConfigWrite write = writes.LastOrDefault(
            candidate => _pathComparer.Equals(candidate.Target, target));
        if (write.Target is null
            || !TryResolveFile(configRoot, write.Target, mustExist: true, out string path, out _))
        {
            return null;
        }

        try
        {
            if (FindMatchingWrite(LoadManifest(configRoot, manifestPath).Manifest, write) is null
                || !TryReadHotloadConfig(path, out string sha256, out string text)
                || !HashesMatch(sha256, write.Sha256))
            {
                return null;
            }

            string[] content = ReadContentLines(text);
            return content.Length == 0 ? null : new(write, configRoot, pluginGuid, content);
        }
        catch (Exception ex) when (IsFileAccessError(ex))
        {
            return null;
        }
    }

    internal static bool RetainAfterHotload(string manifestPath, VShareHotloadConfigSnapshot snapshot,
        Action<VShareConfigWriteStep> checkpoint = null)
    {
        if (snapshot is null)
        {
            return false;
        }

        string manifestTemp = null;
        try
        {
            if (!TryResolveFile(snapshot.ConfigRoot, snapshot.Write.Target, mustExist: true, out string path, out _)
                || !TryReadHotloadConfig(path, out string sha256, out string text)
                || HashesMatch(sha256, snapshot.Write.Sha256))
            {
                return false;
            }

            using var reader = new StringReader(text);
            if (reader.ReadLine()?.StartsWith("## Settings file was created by plugin ", StringComparison.Ordinal) != true
                || !string.Equals(reader.ReadLine(), $"## Plugin GUID: {snapshot.PluginGuid}", StringComparison.Ordinal)
                || !snapshot.ContentLines.SequenceEqual(ReadContentLines(text), StringComparer.Ordinal))
            {
                return false;
            }

            ManifestSnapshot current = LoadManifest(snapshot.ConfigRoot, manifestPath);
            ManagedConfigEntry entry = FindMatchingWrite(current.Manifest, snapshot.Write);
            if (entry is null)
            {
                return false;
            }

            // Record only the bytes observed during this load; future checks still require an exact match.
            entry.Sha256 = sha256;
            manifestTemp = StageManifest(snapshot.ConfigRoot, manifestPath, current.Manifest);
            checkpoint?.Invoke(VShareConfigWriteStep.BeforeManifestPublish);
            RequireFileState(snapshot.ConfigRoot, path, sha256);
            PublishManifest(snapshot.ConfigRoot, manifestTemp, manifestPath, current.Sha256);
            return true;
        }
        catch (Exception ex) when (IsFileAccessError(ex))
        {
            return false;
        }
        finally
        {
            DeleteTempFile(snapshot.ConfigRoot, manifestTemp);
        }
    }

    static ManagedConfigEntry FindMatchingWrite(ManagedConfigManifest manifest, VShareManagedConfigWrite write)
    {
        ManagedConfigEntry entry = manifest.FindEntry(write.PluginKey, write.Source, write.Target);
        return entry is not null && entry.AppliedUtc == write.AppliedUtc
            && HashesMatch(entry.Sha256, write.Sha256)
            ? entry
            : null;
    }

    static bool TryReadHotloadConfig(string path, out string sha256, out string text)
    {
        sha256 = text = string.Empty;
        using var stream = File.OpenRead(path);
        long length = stream.Length;
        if (length <= 0 || length > MAX_HOTLOAD_CONFIG_BYTES)
        {
            return false;
        }

        using var reader = new BinaryReader(stream);
        byte[] bytes = reader.ReadBytes((int)length);
        if (bytes.Length != length)
        {
            return false;
        }

        text = _strictUtf8.GetString(bytes);
        if (text.StartsWith('\uFEFF'))
        {
            text = text[1..];
        }

        sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        return true;
    }

    static string[] ReadContentLines(string text)
    {
        var content = new List<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            {
                content.Add(line);
            }
        }

        return content.ToArray();
    }

    static bool IsFileAccessError(Exception ex)
        => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException or JsonException;

    internal static bool TryResolveServerConfigSourceForTesting(
        string configRoot,
        string pluginKey,
        string source,
        out string sourcePath,
        out string errorMessage)
        => TryResolveServerConfigSource(configRoot, pluginKey, source, out sourcePath, out errorMessage);

    internal static bool TryResolveClientConfigTargetForTesting(
        string configRoot,
        string target,
        out string targetPath,
        out string errorMessage)
    {
        targetPath = string.Empty;
        return TryNormalizeRelativeFile(target, isTarget: true, out string canonical, out errorMessage)
            && TryResolveFile(configRoot, canonical, mustExist: false, out targetPath, out errorMessage);
    }

    internal static VShareConfigApplyResult ApplyClientConfigForTesting(
        byte[] bytes,
        string configRoot,
        string manifestPath,
        string pluginKey,
        string displayName,
        string source,
        string target,
        DateTimeOffset appliedUtc,
        Action<VShareConfigWriteStep> checkpoint = null)
        => ApplyClientConfig(bytes, configRoot, manifestPath, pluginKey, displayName, source, target, appliedUtc, checkpoint);

    static bool TryResolveServerConfigSource(
        string configRoot,
        string pluginKey,
        string source,
        out string sourcePath,
        out string errorMessage)
    {
        sourcePath = string.Empty;
        if (!IsPluginKeyValid(pluginKey))
        {
            errorMessage = "Invalid plugin key for staged config lookup.";
            return false;
        }

        return TryNormalizeRelativeFile(source, isTarget: false, out string canonical, out errorMessage)
            && TryResolveFile(configRoot, $"{SERVER_CONFIGS_DIRECTORY}/{pluginKey}/{canonical}",
                mustExist: true, out sourcePath, out errorMessage);
    }

    static bool TryNormalizeRelativeFile(string path, bool isTarget, out string canonical, out string error)
    {
        canonical = string.Empty;
        if (string.IsNullOrEmpty(path) || path[0] is '/' or '\\' || Path.IsPathRooted(path))
        {
            error = "Config path must be nonempty and relative.";
            return false;
        }

        if (!FitsPacketField(path))
        {
            error = "Config path is invalid UTF-8 or exceeds the packet path length limit.";
            return false;
        }

        string[] segments = path.Split(_pathSeparators);
        if (segments.Any(segment => !IsValidSegment(segment)))
        {
            error = "Config path has an invalid name or a traversal segment.";
            return false;
        }

        if (isTarget && (segments[0].Equals(MyPluginInfo.PLUGIN_NAME, StringComparison.OrdinalIgnoreCase)
            || segments.Any(segment => segment.StartsWith(TRANSACTION_PREFIX, StringComparison.OrdinalIgnoreCase))))
        {
            error = "Config target is reserved by Emberglass.";
            return false;
        }

        canonical = string.Join('/', segments);
        error = string.Empty;
        return true;
    }

    static bool IsPluginKeyValid(string key)
        => !string.IsNullOrEmpty(key) && key.IndexOfAny(_pathSeparators) < 0
            && IsValidSegment(key) && FitsPacketField(key);

    static bool FitsPacketField(string value)
    {
        try
        {
            return _strictUtf8.GetByteCount(value) <= Const.STANDARD_LENGTH;
        }
        catch (EncoderFallbackException) { return false; }
    }

    static bool IsValidSegment(string segment)
    {
        // Exclude Windows path aliases even when validating on a non-Windows server.
        if (segment.Length == 0 || segment[^1] is '.' or ' '
            || segment.IndexOfAny(_invalidNameChars) >= 0 || segment.Any(char.IsControl))
        {
            return false;
        }

        string stem = segment.Split('.', 2)[0].TrimEnd(' ').ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            && !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
                || stem.StartsWith("LPT", StringComparison.Ordinal))
                && "123456789\u00B9\u00B2\u00B3".Contains(stem[3]));
    }

    static bool TryResolveFile(string root, string canonical, bool mustExist, out string path, out string error)
    {
        path = string.Empty;
        try
        {
            string candidate = Path.GetFullPath(Path.Combine(root, canonical.Replace('/', Path.DirectorySeparatorChar)));
            if (!ProbePath(root, candidate, directory: false) && mustExist)
            {
                error = $"Staged config '{canonical}' was not found.";
                return false;
            }

            path = candidate;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsFileAccessError(ex))
        {
            error = ex.Message;
            return false;
        }
    }

    static bool ProbePath(string root, string path, bool directory)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string relative = Path.GetRelativePath(root, path);
        if (relative == ".")
        {
            if (!directory) throw new IOException("Config file cannot be the root directory.");
            return ProbeAttributes(root, directory: true);
        }

        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || segments.Contains(".."))
        {
            throw new IOException("Config path escapes the allowed root.");
        }

        if (!ProbeAttributes(root, directory: true)) return false;
        string current = root;
        for (int i = 0; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
            if (!ProbeAttributes(current, directory || i < segments.Length - 1)) return false;
        }

        return true;
    }

    static bool ProbeAttributes(string path, bool directory)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }

        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
        {
            throw new IOException($"Config path contains an unsupported link or device: '{path}'.");
        }

        if (((attributes & FileAttributes.Directory) != 0) != directory)
        {
            throw new IOException($"Config path does not point to the expected {(directory ? "directory" : "file")}: '{path}'.");
        }

        return true;
    }

    static void EnsureSafeDirectory(string root, string directory)
    {
        if (ProbePath(root, directory, directory: true)) return;
        Directory.CreateDirectory(directory);
        if (!ProbePath(root, directory, directory: true)) throw new IOException("Config directory was not created.");
    }

    static string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    static bool HashesMatch(string first, string second)
        => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    static void RequireFileState(string root, string path, string expectedSha)
    {
        bool exists = ProbePath(root, path, directory: false);
        if (exists != (expectedSha is not null) || (exists && !HashesMatch(HashFile(path), expectedSha)))
        {
            throw new IOException($"File state changed before VShare could finish: '{path}'.");
        }
    }

    static ManifestSnapshot LoadManifest(string root, string path)
    {
        if (!ProbePath(root, path, directory: false))
        {
            return new(new ManagedConfigManifest { Entries = [] }, null);
        }

        byte[] bytes = File.ReadAllBytes(path);
        var manifest = JsonSerializer.Deserialize<ManagedConfigManifest>(_strictUtf8.GetString(bytes).TrimStart('\uFEFF'), _jsonOptions);
        if (manifest?.Entries is null)
        {
            throw new IOException("Managed config manifest has no entries list.");
        }

        foreach (ManagedConfigEntry entry in manifest.Entries)
        {
            if (entry is null || !IsPluginKeyValid(entry.PluginKey)
                || !TryNormalizeRelativeFile(entry.Source, isTarget: false, out string source, out _)
                || !TryNormalizeRelativeFile(entry.Target, isTarget: true, out string target, out _)
                || entry.Sha256?.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit) || entry.AppliedUtc == default)
            {
                throw new IOException("Managed config manifest contains an invalid entry.");
            }

            entry.Source = source;
            entry.Target = target;
        }

        return new(manifest, HashBytes(bytes));
    }

    static string StageManifest(string root, string path, ManagedConfigManifest manifest)
        => StageFile(root, path, JsonSerializer.SerializeToUtf8Bytes(manifest, _jsonOptions));

    static string NewTemporaryPath(string destination, string extension)
        => Path.Combine(Path.GetDirectoryName(destination)!, $"{TRANSACTION_PREFIX}{Guid.NewGuid():N}{extension}");

    static string StageFile(string root, string destination, byte[] bytes)
    {
        EnsureSafeDirectory(root, Path.GetDirectoryName(destination)!);
        ProbePath(root, destination, directory: false);
        string temp = NewTemporaryPath(destination, ".tmp");
        bool created = false;
        try
        {
            using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
            return temp;
        }
        catch
        {
            if (created) DeleteTempFile(root, temp);
            throw;
        }
    }

    static void PublishManifest(string root, string temp, string path, string previousSha)
    {
        RequireFileState(root, path, previousSha);
        PublishFile(root, temp, path, replace: previousSha is not null);
    }

    static void PublishFile(string root, string temp, string path, bool replace, string backup = null)
    {
        if (!ProbePath(root, temp, directory: false) || ProbePath(root, path, directory: false) != replace)
        {
            throw new IOException($"File state changed before publishing '{path}'.");
        }

        if (replace) File.Replace(temp, path, backup);
        else File.Move(temp, path, overwrite: false);
    }

    static void DeleteTempFile(string root, string path)
    {
        if (path is null) return;
        try
        {
            if (ProbePath(root, path, directory: false)) File.Delete(path);
        }
        catch (Exception ex) when (IsFileAccessError(ex))
        {
            VWorld.Log?.LogWarning($"[VShare] Could not remove temporary companion config file '{path}': {ex.Message}");
        }
    }

    readonly record struct ManifestSnapshot(ManagedConfigManifest Manifest, string Sha256);

    sealed class ManagedConfigManifest
    {
        public List<ManagedConfigEntry> Entries { get; set; }

        public ManagedConfigEntry FindEntry(string pluginKey, string source, string target)
        {
            var matches = Entries.Where(entry => _pathComparer.Equals(entry.Target, target)).Take(2).ToArray();
            return matches.Length == 1 && StringComparer.OrdinalIgnoreCase.Equals(matches[0].PluginKey, pluginKey)
                && _pathComparer.Equals(matches[0].Source, source) ? matches[0] : null;
        }

        public void Upsert(ManagedConfigEntry next)
        {
            Entries.RemoveAll(entry => _pathComparer.Equals(entry.Target, next.Target));
            Entries.Add(next);
        }
    }

    sealed class ManagedConfigEntry
    {
        public string PluginKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public DateTimeOffset AppliedUtc { get; set; }
    }
}
