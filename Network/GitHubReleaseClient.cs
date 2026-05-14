using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emberglass.Network;

/// <summary>
/// Provides access to GitHub Release metadata for verifying asset digests.
/// </summary>
public sealed class GitHubReleaseClient
{
    const string GITHUB_API_BASE_URL = "https://api.github.com";
    const string DEFAULT_ACCEPT_HEADER = "application/vnd.github+json";
    const string DEFAULT_USER_AGENT = "Emberglass-GitHubReleaseClient";
    const int REQUEST_TIMEOUT_SECONDS = 15;
    static readonly HttpClient _httpClient = new();
    static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(15);
    readonly Dictionary<ReleaseCacheKey, ReleaseCacheEntry> _digestCache = [];
    readonly object _cacheLock = new();

    static GitHubReleaseClient()
    {
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(DEFAULT_USER_AGENT);
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any(header =>
                header.MediaType?.Equals(DEFAULT_ACCEPT_HEADER, StringComparison.OrdinalIgnoreCase) == true))
        {
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd(DEFAULT_ACCEPT_HEADER);
        }
    }

    /// <summary>
    /// Begins an asynchronous lookup for the GitHub Release asset digest.
    /// </summary>
    /// <param name="identity">The release identity.</param>
    /// <param name="assetFileName">The expected asset file name.</param>
    /// <param name="cancellationToken">A cancellation token for the network request.</param>
    /// <param name="onComplete">Callback invoked with the digest lookup result.</param>
    public void BeginReleaseAssetDigestLookup(
        GitHubReleaseIdentity identity,
        string assetFileName,
        CancellationToken cancellationToken,
        Action<GitHubReleaseDigestResult> onComplete)
    {
        if (onComplete is null)
        {
            throw new ArgumentNullException(nameof(onComplete));
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            GitHubReleaseDigestResult result = ExecuteReleaseAssetDigestLookup(identity, assetFileName, cancellationToken);
            onComplete(result);
        });
    }

    /// <summary>
    /// Retrieves the GitHub Release asset digest for the requested asset.
    /// </summary>
    /// <param name="identity">The release identity.</param>
    /// <param name="assetFileName">The expected asset file name.</param>
    /// <param name="cancellationToken">A cancellation token for the network request.</param>
    /// <returns>The digest lookup result.</returns>
    public GitHubReleaseDigestResult GetReleaseAssetDigest(
        GitHubReleaseIdentity identity,
        string assetFileName,
        CancellationToken cancellationToken) =>
        ExecuteReleaseAssetDigestLookup(identity, assetFileName, cancellationToken);

    /// <summary>
    /// Describes the GitHub release identity used to locate assets.
    /// </summary>
    public readonly record struct GitHubReleaseIdentity(string Owner, string Repo, string Tag);

    /// <summary>
    /// Represents the outcome of a GitHub Release asset digest lookup.
    /// </summary>
    public readonly record struct GitHubReleaseDigestResult(bool IsSuccess, string Digest, string Tag, string ErrorMessage)
    {
        /// <summary>
        /// Creates a success result with the specified digest and tag.
        /// </summary>
        /// <param name="digest">The asset digest.</param>
        /// <param name="tag">The release tag.</param>
        /// <returns>A success result.</returns>
        public static GitHubReleaseDigestResult Success(string digest, string tag) =>
            new(true, digest, tag, string.Empty);

        /// <summary>
        /// Creates a failure result with the specified tag and error message.
        /// </summary>
        /// <param name="tag">The release tag.</param>
        /// <param name="errorMessage">The failure description.</param>
        /// <returns>A failure result.</returns>
        public static GitHubReleaseDigestResult Failure(string tag, string errorMessage) =>
            new(false, string.Empty, tag, errorMessage);
    }

    GitHubReleaseDigestResult ExecuteReleaseAssetDigestLookup(
        GitHubReleaseIdentity identity,
        string assetFileName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identity.Owner) ||
            string.IsNullOrWhiteSpace(identity.Repo) ||
            string.IsNullOrWhiteSpace(identity.Tag))
        {
            return GitHubReleaseDigestResult.Failure(identity.Tag, "GitHub release identity is missing required data.");
        }

        if (string.IsNullOrWhiteSpace(assetFileName))
        {
            return GitHubReleaseDigestResult.Failure(identity.Tag, "GitHub release asset name was empty.");
        }

        try
        {
            if (TryGetCachedDigest(identity, assetFileName, out GitHubReleaseDigestResult cachedResult))
            {
                return cachedResult;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return GitHubReleaseDigestResult.Failure(identity.Tag, "GitHub Release asset digest lookup was cancelled.");
            }

            string url = $"{GITHUB_API_BASE_URL}/repos/{identity.Owner}/{identity.Repo}/releases/tags/{identity.Tag}";
            using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutTokenSource.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = _httpClient.Send(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                return GitHubReleaseDigestResult.Failure(
                    identity.Tag,
                    $"GitHub Release request failed with status code {(int)response.StatusCode} ({response.StatusCode}).");
            }

            using var responseStream = response.Content.ReadAsStream();
            GitHubReleaseResponse responseData = JsonSerializer.Deserialize<GitHubReleaseResponse>(
                responseStream,
                _serializerOptions);

            GitHubReleaseAsset matchedAsset = responseData?.Assets
                ?.FirstOrDefault(asset => string.Equals(asset.Name, assetFileName, StringComparison.OrdinalIgnoreCase));

            if (matchedAsset is null)
            {
                return GitHubReleaseDigestResult.Failure(
                    identity.Tag,
                    $"GitHub Release asset metadata did not include '{assetFileName}'.");
            }

            if (!TryGetDigestFromAsset(matchedAsset, out string digest, out string digestError))
            {
                return GitHubReleaseDigestResult.Failure(identity.Tag, digestError);
            }

            var result = GitHubReleaseDigestResult.Success(digest, identity.Tag);
            CacheDigest(identity, assetFileName, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            return GitHubReleaseDigestResult.Failure(identity.Tag, "GitHub Release asset digest lookup was cancelled.");
        }
        catch (Exception ex)
        {
            return GitHubReleaseDigestResult.Failure(identity.Tag, $"GitHub Release asset digest lookup failed: {ex.Message}");
        }
    }

    static bool TryGetDigestFromAsset(GitHubReleaseAsset asset, out string digest, out string errorMessage)
    {
        digest = asset.Digest;
        if (string.IsNullOrWhiteSpace(digest))
        {
            digest = asset.Sha256;
        }

        if (string.IsNullOrWhiteSpace(digest))
        {
            errorMessage =
                "GitHub Release asset metadata did not contain a SHA-256 digest. " +
                "Ensure the asset metadata includes a 'digest' or 'sha256' field.";
            return false;
        }

        digest = digest.Trim();
        errorMessage = string.Empty;
        return true;
    }

    bool TryGetCachedDigest(
        GitHubReleaseIdentity identity,
        string assetFileName,
        out GitHubReleaseDigestResult result)
    {
        var key = new ReleaseCacheKey(identity.Owner, identity.Repo, identity.Tag, assetFileName);
        lock (_cacheLock)
        {
            if (_digestCache.TryGetValue(key, out ReleaseCacheEntry entry) &&
                DateTimeOffset.UtcNow <= entry.ExpiresAt)
            {
                result = GitHubReleaseDigestResult.Success(entry.Digest, entry.Tag);
                return true;
            }
        }

        result = default;
        return false;
    }

    void CacheDigest(GitHubReleaseIdentity identity, string assetFileName, GitHubReleaseDigestResult result)
    {
        if (!result.IsSuccess)
        {
            return;
        }

        var key = new ReleaseCacheKey(identity.Owner, identity.Repo, identity.Tag, assetFileName);
        var entry = new ReleaseCacheEntry(result.Digest, result.Tag, DateTimeOffset.UtcNow.Add(_cacheTtl));
        lock (_cacheLock)
        {
            _digestCache[key] = entry;
        }
    }

    record struct ReleaseCacheKey(string Owner, string Repo, string Tag, string AssetName);
    record struct ReleaseCacheEntry(string Digest, string Tag, DateTimeOffset ExpiresAt);

    sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; init; } = [];
    }

    sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("digest")]
        public string Digest { get; init; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;
    }
}
