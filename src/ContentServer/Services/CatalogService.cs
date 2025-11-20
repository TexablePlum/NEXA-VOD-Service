using ContentServer.Exceptions;
using Nexa.Shared.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StackExchange.Redis;
using Microsoft.AspNetCore.OutputCaching;

namespace Nexa.ContentServer.Services
{
    /// <summary>
    /// Serwis do zarządzania katalogiem filmów.
    /// Czyta metadata.json i zwraca listę dostępnych treści.
    /// Implementuje caching w Redis z automatycznym refreshem danych przy zmianach w storage.
    /// Inwaliduje Output Cache przy zmianach w plikach storage.
    /// </summary>
    public class CatalogService : IDisposable
    {
        private readonly string _basePath;
        private readonly ILogger<CatalogService> _logger;
        private readonly IDatabase _redisDb;
        private readonly TimeSpan _cacheDuration;
        private readonly FileSystemWatcher _watcher;
        private readonly IOutputCacheStore? _outputCacheStore;

        private const string CacheKeyContentIds = "catalog:ids";
        private string GetCacheKeyForContent(string contentId) => $"catalog:id:{contentId}";
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public CatalogService(
            string basePath,
            ILogger<CatalogService> logger,
            IDatabase redisDb,
            TimeSpan cacheDuration,
            IOutputCacheStore? outputCacheStore = null)
        {
            _basePath = basePath;
            _logger = logger;
            _redisDb = redisDb;
            _cacheDuration = cacheDuration;
            _outputCacheStore = outputCacheStore;

            _watcher = new FileSystemWatcher(_basePath)
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Renamed += (s, e) => OnFileSystemChanged(s, e);
        }

        /// <summary>
        /// Obsługuje zmiany w file system - inwaliduje cache tylko dla istotnych zmian.
        /// Tylko metadata.json i foldery contentów.
        /// </summary>
        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            var fileName = Path.GetFileName(e.FullPath);

            // Inwaliduje cache tylko gdy:
            // 1. Zmieniono/dodano/usunięto metadata.json
            // 2. Dodano/usunięto folder contentu
            if (fileName == "metadata.json" ||
                Path.GetDirectoryName(e.FullPath) == Path.GetFullPath(_basePath))
            {
                InvalidateCache($"{e.ChangeType}: {e.Name}");
            }
            else
            {
                // Ignoruje zmiany w segmentach wideo, miniaturach, etc.
                _logger.LogDebug("Ignoring file system change: {ChangeType} {Name}", e.ChangeType, e.Name);
            }
        }

        /// <summary>
        /// Pobiera listę filmów z folderu storage z paginacją i opcjonalnym wyszukiwaniem.
        /// cache-uje listę ID, zastosowuje paginację na ID (jeśli brak search),
        /// a następnie pobiera tylko potrzebne treści (z cache lub dysku).
        /// </summary>
        public async Task<List<ContentMetadata>> GetAllContentAsync(
            int limit,
            int offset,
            string? search = null,
            CancellationToken cancellationToken = default)
        {
            // Pobiera listę wszystkich ContentId
            var allContentIds = await GetContentIdsAsync(cancellationToken);

            // Jeśli nie użyto search
            if (string.IsNullOrWhiteSpace(search))
            {
                var paginatedIds = allContentIds
                    .Skip(offset)
                    .Take(limit)
                    .ToList();

                _logger.LogInformation("Loading {Count} content items (out of {Total}) with pagination, no search",
                    paginatedIds.Count, allContentIds.Count);

                return await LoadContentsByIdsAsync(paginatedIds, cancellationToken);
            }

            // Jeśli użyto search ładuje wszytsko, filtruje, potem paginuje
            _logger.LogInformation("Loading all {Count} content items for search filtering", allContentIds.Count);

            var allContent = await LoadContentsByIdsAsync(allContentIds, cancellationToken);

            var filteredContent = allContent
                .Where(c => c.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Skip(offset)
                .Take(limit)
                .ToList();

            _logger.LogInformation("Returned {Count} content items after search filter for '{Search}'",
                filteredContent.Count, search);

            return filteredContent;
        }

        /// <summary>
        /// Pobiera total count wszystkich filmów (z opcjonalnym filtrem search).
        /// </summary>
        public async Task<int> GetTotalCountAsync(string? search = null, CancellationToken cancellationToken = default)
        {
            var allContentIds = await GetContentIdsAsync(cancellationToken);

            // Jeśli brak search, zwróć count IDs
            if (string.IsNullOrWhiteSpace(search))
            {
                return allContentIds.Count;
            }

            // Jeśli jest search, musimy załadować wszystkie i przefiltrować
            var allContent = await LoadContentsByIdsAsync(allContentIds, cancellationToken);

            var filteredCount = allContent
                .Count(c => c.Title.Contains(search, StringComparison.OrdinalIgnoreCase));

            return filteredCount;
        }

        /// <summary>
        /// Pobiera listę wszystkich ContentId z cache lub dysku.
        /// </summary>
        private async Task<List<string>> GetContentIdsAsync(CancellationToken cancellationToken)
        {
            // Sprawdź cache dla listy ContentIds
            var cachedIds = await _redisDb.StringGetAsync(CacheKeyContentIds);

            if (cachedIds.HasValue)
            {
                _logger.LogInformation("Cache HIT for content IDs list");
                // Bezpieczna deserializacja z obsługą null i wyjątków
                try
                {
                    var cacheValue = cachedIds.ToString();
                    if (!string.IsNullOrEmpty(cacheValue))
                    {
                        var idsList = JsonSerializer.Deserialize<List<string>>(cacheValue, _jsonOptions);
                        if (idsList != null)
                        {
                            return idsList;
                        }
                    }
                    _logger.LogWarning("Cached content IDs deserialized to null, fetching from disk");
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize cached content IDs, fetching from disk");
                }
            }

            _logger.LogInformation("Cache MISS for content IDs. Scanning storage directories.");

            // Pobiera listę folderów (każdy folder to ContentId)
            if (!Directory.Exists(_basePath))
            {
                _logger.LogError("Storage path does not exist: {Path}", _basePath);
                throw new StorageUnavailableException(_basePath);
            }

            var contentDirectories = Directory.GetDirectories(_basePath);
            var contentIds = contentDirectories
                .Select(Path.GetFileName)
                .Where(id => !string.IsNullOrEmpty(id))
                .Cast<string>()
                .ToList();

            // Cacheuje listę ID
            var serializedIds = JsonSerializer.Serialize(contentIds);
            await _redisDb.StringSetAsync(CacheKeyContentIds, serializedIds, _cacheDuration);
            _logger.LogInformation("Cached {Count} content IDs", contentIds.Count);

            return contentIds;
        }

        /// <summary>
        /// Ładuje contenty dla podanych ID (z cache lub dysku).
        /// </summary>
        private async Task<List<ContentMetadata>> LoadContentsByIdsAsync(
            List<string> contentIds,
            CancellationToken cancellationToken)
        {
            var loadTasks = contentIds.Select(async contentId =>
            {
                try
                {
                    return await GetContentByIdAsync(contentId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load content {ContentId}", contentId);
                    return null;
                }
            });

            var results = await Task.WhenAll(loadTasks);
            return results.Where(m => m != null).Cast<ContentMetadata>().ToList();
        }

        /// <summary>
        /// Pobiera metadane konkretnego filmu po ID.
        /// Rzuca ContentNotFoundException jeśli nie znaleziono.
        /// </summary>
        public async Task<ContentMetadata> GetContentByIdAsync(string contentId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(contentId))
            {
                throw new ValidationException("Content ID cannot be empty");
            }

            // Path traversal protection
            if (contentId.Contains("..") || contentId.Contains("/") || contentId.Contains("\\"))
            {
                _logger.LogWarning("Path traversal attempt blocked in catalog: {ContentId}", contentId);
                throw new ValidationException("Invalid content ID format");
            }

            var cacheKey = GetCacheKeyForContent(contentId);

            var cachedData = await _redisDb.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                _logger.LogInformation("Cache HIT for key: {CacheKey}", cacheKey);
                // Bezpieczna deserializacja z obsługą null i wyjątków
                try
                {
                    var cacheValue = cachedData.ToString();
                    if (!string.IsNullOrEmpty(cacheValue))
                    {
                        var cachedItem = JsonSerializer.Deserialize<ContentMetadata>(cacheValue, _jsonOptions);
                        if (cachedItem != null)
                        {
                            return cachedItem;
                        }
                    }
                    _logger.LogWarning("Cached content metadata for {ContentId} deserialized to null, fetching from disk", contentId);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize cached content metadata for {ContentId}, fetching from disk", contentId);
                }
            }

            _logger.LogInformation("Cache MISS for key: {CacheKey}. Fetching from disk.", cacheKey);

            var metadata = await FetchContentByIdFromDiskAsync(contentId, cancellationToken);

            var serializedData = JsonSerializer.Serialize(metadata);
            await _redisDb.StringSetAsync(cacheKey, serializedData, _cacheDuration);
            _logger.LogInformation("Stored item in cache for key: {CacheKey}", cacheKey);

            return metadata;
        }

        private void InvalidateCache(string reason)
        {
            _logger.LogInformation("Invalidating cache. Reason: {Reason}", reason);

            // 1. Kasuje Redis cache
            // Kasuje listę ContentId
            _redisDb.KeyDelete(CacheKeyContentIds);

            // Kasuje wszystkie indywidualne cacheowane contenty
            var server = _redisDb.Multiplexer.GetServer(_redisDb.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: "catalog:id:*").ToArray();

            if (keys.Length > 0)
            {
                _redisDb.KeyDelete(keys);
                _logger.LogInformation("Invalidated {Count} individual Redis cache entries", keys.Length);
            }

            // 2. Kasuje Output Cache (HTTP responses) z tagiem "catalog"
            if (_outputCacheStore != null)
            {
                try
                {
                    // Nie czeka na wynik
                    _ = _outputCacheStore.EvictByTagAsync("catalog", default);
                    _logger.LogInformation("Invalidated Output Cache for tag 'catalog'");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to invalidate Output Cache");
                }
            }
        }

        private async Task<ContentMetadata> FetchContentByIdFromDiskAsync(string contentId, CancellationToken cancellationToken)
        {
            var metadataPath = Path.Combine(_basePath, contentId, "metadata.json");

            // Dodatkowa weryfikacja path traversal na poziomie fizycznego dostępu
            var fullBasePath = Path.GetFullPath(_basePath);
            var fullMetadataPath = Path.GetFullPath(metadataPath);

            if (!fullMetadataPath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Path traversal attack detected in catalog: {ContentId}", contentId);
                throw new ContentNotFoundException(contentId);
            }

            if (!File.Exists(metadataPath))
            {
                _logger.LogWarning("Content not found on disk: {ContentId}", contentId);
                throw new ContentNotFoundException(contentId);
            }

            try
            {
                var jsonContent = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                var metadata = JsonSerializer.Deserialize<ContentMetadata>(jsonContent, _jsonOptions);

                if (metadata == null)
                {
                    _logger.LogError("Failed to deserialize metadata for {ContentId}", contentId);
                    throw new ContentNotFoundException(contentId);
                }

                return metadata;
            }
            catch (ContentNotFoundException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading content metadata from disk for {ContentId}", contentId);
                throw;
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}