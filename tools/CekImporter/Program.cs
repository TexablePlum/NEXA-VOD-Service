using StackExchange.Redis;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Parsowanie argumentów
if (args.Length < 2)
{
    Console.WriteLine("Usage: CekImporter <content-storage-path> <cek-master-key-base64> [redis-host] [redis-password]");
    Console.WriteLine("Example: CekImporter C:\\content\\storage rkf45P7MB... localhost:6379 mypassword");
    Console.WriteLine("\nGenerate master key: openssl rand -base64 32");
    return;
}

var storagePath = args[0];
var masterKeyBase64 = args[1];
var redisHost = args.Length > 2 ? args[2] : "localhost:6379";
var redisPassword = args.Length > 3 ? args[3] : null;

Console.WriteLine("========================================");
Console.WriteLine("NEXA - CEK Importer (with encryption)");
Console.WriteLine("========================================");
Console.WriteLine($"Storage: {storagePath}");
Console.WriteLine($"Redis: {redisHost}");

// Walidacja master key
byte[] masterKey;
try
{
    masterKey = Convert.FromBase64String(masterKeyBase64);
    if (masterKey.Length != 32)
    {
        Console.WriteLine($"✗ CEK Master Key must be 32 bytes (256 bits). Current: {masterKey.Length} bytes");
        Console.WriteLine("Generate with: openssl rand -base64 32");
        return;
    }
    Console.WriteLine("✓ CEK Master Key loaded (256-bit)\n");
}
catch (FormatException)
{
    Console.WriteLine("✗ Invalid CEK Master Key format. Must be Base64 string.");
    Console.WriteLine("Generate with: openssl rand -base64 32");
    return;
}

// Łączenie z Redis
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

// Skanowanie katalogów content
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
        // Wczytanie metadata
        var metadataJson = File.ReadAllText(metadataFile);
        var metadata = JsonDocument.Parse(metadataJson);
        var requiredPlan = metadata.RootElement.GetProperty("RequiredPlan").GetString() ?? "free";

        // Wczytanie encryption
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
        var qualitiesSetKey = $"content:qualities:{contentId}";

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

            var cekPlaintext = File.ReadAllText(keyFile).Trim();

            // Szyfruje CEK za pomocą master keya
            string encryptedCek;
            try
            {
                encryptedCek = EncryptCek(cekPlaintext, masterKey);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ERROR] {qualityName} - encryption failed: {ex.Message}");
                continue;
            }

            // Zapisuje zaszyfrowany CEK w nowym formacie
            var cekKey = $"cek:{contentId}:{qualityName}";
            var cekValue = JsonSerializer.Serialize(new { EncryptedKey = encryptedCek, KeyId = keyId });
            db.StringSet(cekKey, cekValue);

            // Dodaje jakość do Redis SET (dla szybkiego listingu)
            db.SetAdd(qualitiesSetKey, qualityName);

            Console.WriteLine($"  ✓ {qualityName}: KID={keyId} (encrypted)");
            qualityCount++;
        }

        Console.WriteLine($"  Imported {qualityCount} qualities (encrypted + SET)\n");
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

// ===========================
// CEK Encryption Function
// ===========================
static string EncryptCek(string plaintext, byte[] masterKey)
{
    using var aes = new AesGcm(masterKey, AesGcm.TagByteSizes.MaxSize);

    var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
    var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
    var ciphertext = new byte[plaintextBytes.Length];
    var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

    RandomNumberGenerator.Fill(nonce);
    aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

    // Format: nonce(12) + tag(16) + ciphertext
    var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
    Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
    Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
    Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);

    return Convert.ToBase64String(result);
}
