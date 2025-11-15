using ContentServer.Exceptions;
using Nexa.Shared.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using StackExchange.Redis;

namespace Nexa.ContentServer.Services
{
    /// <summary>
    /// Serwis do zarządzania katalogiem filmów.
    /// Czyta metadata.json i zwraca listę dostępnych treści.
    /// Implementuje caching w Redis z automatycznym refreshem danych przy zmianach w storage.
    /// </summary>
    public class CatalogService : IDisposable
    {
        private readonly string _basePath;
        private readonly ILogger<CatalogService> _logger;
        private readonly IDatabase _redisDb;
        private readonly TimeSpan _cacheDuration;
        private readonly FileSystemWatcher _watcher;

        private const string CacheKeyAllContent = "catalog:all";
        private string GetCacheKeyForContent(string contentId) => $"catalog:id:{contentId}";
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        public CatalogService(string basePath, ILogger<CatalogService> logger, IDatabase redisDb, TimeSpan cacheDuration)
        {
            _basePath = basePath;
            _logger = logger;
            _redisDb = redisDb;
            _cacheDuration = cacheDuration;

            _watcher = new FileSystemWatcher(_basePath)
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += (s, e) => InvalidateCache("File created: " + e.Name);
            _watcher.Deleted += (s, e) => InvalidateCache("File deleted: " + e.Name);
            _watcher.Changed += (s, e) => InvalidateCache("File changed: " + e.Name);
            _watcher.Renamed += (s, e) => InvalidateCache("File renamed: " + e.Name);
        }

        /// <summary>
        /// Pobiera listę wszystkich filmów z folderu storage.
        /// Najpierw sprawdza cache w Redis, jeśli nie ma to czyta dane z dysku.
        /// </summary>
        public async Task<List<ContentMetadata>> GetAllContentAsync(CancellationToken cancellationToken = default)
        {
            var cachedData = await _redisDb.StringGetAsync(CacheKeyAllContent);
            if (cachedData.HasValue)
            {
                _logger.LogInformation("Cache HIT for key: {CacheKey}", CacheKeyAllContent);
                var cachedList = JsonSerializer.Deserialize<List<ContentMetadata>>(cachedData.ToString()!, _jsonOptions);
                if (cachedList != null)
                {
                    return cachedList;
                }
            }

            _logger.LogInformation("Cache MISS for key: {CacheKey}. Fetching from disk.", CacheKeyAllContent);

            var contentList = await FetchAllContentFromDiskAsync(cancellationToken);

            var serializedData = JsonSerializer.Serialize(contentList);
            await _redisDb.StringSetAsync(CacheKeyAllContent, serializedData, _cacheDuration);
            _logger.LogInformation("Stored {Count} items in cache for key: {CacheKey}", contentList.Count, CacheKeyAllContent);

            return contentList;
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

            var cacheKey = GetCacheKeyForContent(contentId);

            var cachedData = await _redisDb.StringGetAsync(cacheKey);
            if (cachedData.HasValue)
            {
                _logger.LogInformation("Cache HIT for key: {CacheKey}", cacheKey);
                var cachedItem = JsonSerializer.Deserialize<ContentMetadata>(cachedData.ToString()!, _jsonOptions);
                if (cachedItem != null)
                {
                    return cachedItem;
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
            _redisDb.KeyDelete(CacheKeyAllContent);
        }

        private async Task<List<ContentMetadata>> FetchAllContentFromDiskAsync(CancellationToken cancellationToken)
        {
            if (!Directory.Exists(_basePath))
            {
                _logger.LogError("Storage path does not exist: {Path}", _basePath);
                throw new StorageUnavailableException(_basePath);
            }

            try
            {
                var contentDirectories = Directory.GetDirectories(_basePath);

                var loadTasks = contentDirectories.Select(async contentDir =>
                {
                    var metadataPath = Path.Combine(contentDir, "metadata.json");

                    if (!File.Exists(metadataPath))
                    {
                        _logger.LogWarning("Metadata not found for content: {Dir}", contentDir);
                        return null;
                    }

                    try
                    {
                        var jsonContent = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                        return JsonSerializer.Deserialize<ContentMetadata>(jsonContent, _jsonOptions);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse metadata.json in {Dir}", contentDir);
                        return null;
                    }
                });

                var results = await Task.WhenAll(loadTasks);
                var contentList = results.Where(m => m != null).Cast<ContentMetadata>().ToList();

                _logger.LogInformation("Loaded {Count} content items from storage (disk)", contentList.Count);

                return contentList;
            }
            catch (StorageUnavailableException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading content catalog from disk");
                throw;
            }
        }

        private async Task<ContentMetadata> FetchContentByIdFromDiskAsync(string contentId, CancellationToken cancellationToken)
        {
            var metadataPath = Path.Combine(_basePath, contentId, "metadata.json");

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