using StackExchange.Redis;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

// Parsowanie argumentów
if (args.Length < 1)
{
    Console.WriteLine("Usage: CekImporter <content-storage-path> [redis-host] [redis-password]");
    Console.WriteLine("Example: CekImporter C:\\content\\storage localhost:6379 mypassword");
    return;
}

var storagePath = args[0];
var redisHost = args.Length > 1 ? args[1] : "localhost:6379";
var redisPassword = args.Length > 2 ? args[2] : null;

Console.WriteLine("========================================");
Console.WriteLine("NEXA - CEK Importer");
Console.WriteLine("========================================");
Console.WriteLine($"Storage: {storagePath}");
Console.WriteLine($"Redis: {redisHost}");

// Połącz z Redis
var configOptions = ConfigurationOptions.Parse(redisHost);
if (!string.IsNullOrEmpty(redisPassword))
{
    configOptions.Password = redisPassword;
}
configOptions.ConnectTimeout = 5000;

IConnectionMultiplexer redis;
try
{
    redis = ConnectionMultiplexer.Connect(configOptions);
    Console.WriteLine("✓ Connected to Redis\n");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Failed to connect to Redis: {ex.Message}");
    return;
}

var db = redis.GetDatabase();

// Skanuj katalogi content
if (!Directory.Exists(storagePath))
{
    Console.WriteLine($"✗ Storage path does not exist: {storagePath}");
    return;
}

var contentDirs = Directory.GetDirectories(storagePath);
Console.WriteLine($"Found {contentDirs.Length} content directories\n");

int totalImported = 0;

foreach (var contentDir in contentDirs)
{
    var contentId = Path.GetFileName(contentDir);
    var metadataFile = Path.Combine(contentDir, "metadata.json");
    var encryptionFile = Path.Combine(contentDir, "encryption.json");

    if (!File.Exists(encryptionFile))
    {
        Console.WriteLine($"[SKIP] {contentId} - no encryption.json (unencrypted)");
        continue;
    }

    if (!File.Exists(metadataFile))
    {
        Console.WriteLine($"[SKIP] {contentId} - no metadata.json");
        continue;
    }

    try
    {
        // Wczytaj metadata
        var metadataJson = File.ReadAllText(metadataFile);
        var metadata = JsonDocument.Parse(metadataJson);
        var requiredPlan = metadata.RootElement.GetProperty("RequiredPlan").GetString() ?? "free";

        // Wczytaj encryption
        var encryptionJson = File.ReadAllText(encryptionFile);
        var encryption = JsonDocument.Parse(encryptionJson);
        var qualities = encryption.RootElement.GetProperty("Qualities");

        Console.WriteLine($"[IMPORT] {contentId} (plan: {requiredPlan})");

        // Import content metadata
        var contentMetaKey = $"content:meta:{contentId}";
        var contentMetaValue = JsonSerializer.Serialize(new { RequiredPlan = requiredPlan });
        db.StringSet(contentMetaKey, contentMetaValue);

        // Import CEK-ów dla każdej jakości
        int qualityCount = 0;
        foreach (var quality in qualities.EnumerateObject())
        {
            var qualityName = quality.Name;
            var keyId = quality.Value.GetProperty("KeyId").GetString();
            var keyFile = Path.Combine(contentDir, qualityName, $"{qualityName}.key");

            if (!File.Exists(keyFile))
            {
                Console.WriteLine($"  [WARN] {qualityName} - key file not found: {keyFile}");
                continue;
            }

            var cek = File.ReadAllText(keyFile).Trim();

            var cekKey = $"cek:{contentId}:{qualityName}";
            var cekValue = JsonSerializer.Serialize(new { Key = cek, KeyId = keyId });
            db.StringSet(cekKey, cekValue);

            Console.WriteLine($"  ✓ {qualityName}: KID={keyId}");
            qualityCount++;
        }

        Console.WriteLine($"  Imported {qualityCount} qualities\n");
        totalImported++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [ERROR] Failed to import {contentId}: {ex.Message}\n");
    }
}

redis.Close();

Console.WriteLine("========================================");
Console.WriteLine($"✓ Import completed: {totalImported} contents");
Console.WriteLine("========================================");
