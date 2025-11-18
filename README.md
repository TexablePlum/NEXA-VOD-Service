
# Dokumentacja modułów NEXA VOD

## Spis treści

1. [Content Server](#1-content-server)
2. [Skrypt prepare-content.ps1](#2-skrypt-prepare-contentps1)
3. [Biblioteka Shared](#3-biblioteka-shared)

---

## 1. Content Server

Serwer ASP.NET Core 9.0 obsługujący katalog filmów i streaming MPEG-DASH mechanizmy wydajnościowymi i bezpieczeństwa.

### Architektura

```
┌─────────────────────────────────────────────────┐
│           Content Server (ASP.NET)              │
├─────────────────────────────────────────────────┤
│  Controllers                                    │
│  ├── CatalogController    (/api/catalog)        │
│  └── StreamingController  (/content/{id}/...)   │
│                                                 │
│  Services                                       │
│  ├── CatalogService (dwupoziomowy cache)        │
│  └── StreamingService (range requests)          │
│                                                 │
│  Middleware Pipeline                            │
│  ├── ResponseCompression (Brotli/Gzip)          │
│  ├── OutputCache (RAM, 100MB limit)             │
│  ├── ErrorHandling                              │
│  └── RateLimiting (per-IP)                      │
└─────────────────────────────────────────────────┘
         │                          │
         ▼                          ▼
   ┌──────────┐            ┌─────────────────┐
   │  Redis   │            │ Content Storage │
   │  (L2)    │            │  (File System)  │
   └──────────┘            └─────────────────┘
```

### Mechanizmy wydajnościowe

#### 1. Dwupoziomowy cache

```
Request → Output Cache (L1, RAM, 100MB) → Redis (L2, TTL 3600s) → Dysk
```

| Warstwa | Technologia | Zastosowanie | TTL |
|---------|-------------|--------------|-----|
| **L1** | Output Cache | Gotowe odpowiedzi HTTP | 5min - 24h |
| **L2** | Redis | Zdeserializowane obiekty | 3600s |

**Strategie cache:**
- `/api/catalog` - 5 min (tag: `catalog`, warianty: `limit,offset,search`)
- `/content/{id}/manifest.mpd` - 5 min
- `/content/{id}/{quality}/init_*.m4s` - **24h** (init segments rzadko się zmieniają)
- `/content/{id}/thumbnail.jpg` - 1h

**Invalidacja automatyczna:**

FileSystemWatcher nasłuchuje **wszystkich** zmian w `content/storage/`, ale invaliduje cache **tylko** gdy:
- Zmieniono `metadata.json` (nowy tytuł, opis, jakości)
- Dodano/usunięto folder contentu (nowy/usunięty film)

Ignoruje:
- Dodanie segmentów `.m4s` (setki plików podczas prepare-content.ps1)
- Zmiany kluczy `.key` (nie wpływa na katalog)
- Miniaturki, manifesty (nie wpływa na metadane katalogowe)

**Efekt:** Podczas dodawania filmu cache invaliduje się **1 raz** (przy zapisie `metadata.json`) zamiast **200+ razy** (przy każdym segmencie).

#### 2. Lazy loading z paginacją

Bez wyszukiwania:
```
1. Pobierz listę ID z cache/dysku (CacheKeyContentIds)
2. Zastosuj offset/limit na ID
3. Równolegle załaduj tylko potrzebne contenty (Task.WhenAll)
```

Z wyszukiwaniem:
```
1. Pobierz wszystkie ID
2. Równolegle załaduj wszystkie contenty
3. Filtruj po Title (case-insensitive)
4. Zastosuj offset/limit
```

**Korzyści:**
- Bez search: ładuje tylko N contentów zamiast wszystkich
- Z search: ładuje wszystkie, ale równolegle (Task.WhenAll)

#### 3. Kompresja odpowiedzi

```csharp
Brotli/Gzip (CompressionLevel.Fastest)
  ↓
MIME types: application/json, application/dash+xml
  ↓
EnableForHttps = true
```

**Nie kompresuje:** `video/*`, `audio/*`, `image/*` (już skompresowane)

#### 4. Request timeouts

```csharp
ConnectTimeout: 5000ms (Redis)
SyncTimeout: 5000ms (Redis)
HeadersTimeout: 30s (Kestrel)
KeepAliveTimeout: 2min
```

### Mechanizmy bezpieczeństwa

#### 1. Wielopoziomowa ochrona Path Traversal

**Poziom 1 - Walidacja wejścia:**
```csharp
if (contentId.Contains("..") || contentId.Contains("/") || contentId.Contains("\\"))
    throw new ValidationException("Invalid content ID format");
```

**Poziom 2 - Weryfikacja fizycznej ścieżki:**
```csharp
var fullBasePath = Path.GetFullPath(_basePath);
var fullMetadataPath = Path.GetFullPath(metadataPath);

if (!fullMetadataPath.StartsWith(fullBasePath))
    throw new ContentNotFoundException(contentId);
```

#### 2. Rate Limiting (per-IP)

| Okno czasowe | Limit | Reakcja |
|--------------|-------|---------|
| 1 minuta | 100 żądań | HTTP 429 |
| 15 minut | 1000 żądań | HTTP 429 |

**Implementacja:** `AspNetCoreRateLimit` z `AsyncKeyLockProcessingStrategy`

#### 3. Redis jako wymagany komponent

```csharp
AbortOnConnectFail = true  // Brak fallback - fail fast
```

Jeśli Redis nie odpowiada → aplikacja nie startuje. Gwarantuje spójność cache w środowisku rozproszonym.

#### 4. CancellationToken w całej warstwie serwisowej

```csharp
Task<ContentMetadata> GetContentByIdAsync(string id, CancellationToken ct)
Task<List<ContentMetadata>> GetAllContentAsync(..., CancellationToken ct)
```

Umożliwia anulowanie długotrwałych operacji (np. client disconnect).

### Health Checks

| Nazwa | Sprawdza | Endpoint |
|-------|----------|----------|
| **self** | Czy aplikacja działa | `/health` |
| **storage** | Dostępność folderu storage | `/health` |
| **redis** | Połączenie z Redis | `/health` |

**Format odpowiedzi:**
```json
{
  "status": "Healthy",
  "checks": [
    { "name": "storage", "status": "Healthy", "duration": 2.5 }
  ],
  "totalDuration": 15.3
}
```

### Endpointy

| Metoda | Ścieżka | Parametry | Cache |
|--------|---------|-----------|-------|
| GET | `/api/catalog` | `limit`, `offset`, `search` | 5 min (L1+L2) |
| GET | `/api/catalog/{id}` | - | 5 min (L1+L2) |
| GET | `/content/{id}/manifest.mpd` | - | 5 min (L1) |
| GET | `/content/{id}/{quality}/{segment}` | - | 24h dla `init_*` |
| GET | `/content/{id}/thumbnail.jpg` | - | 1h (L1) |
| GET | `/health` | - | Brak |

**Range Requests:** StreamingController obsługuje `HTTP Range` dla segmentów wideo (seeking w playerze).

---

## 2. Skrypt prepare-content.ps1

Kompletny pipeline przetwarzania wideo: transkodowanie, segmentacja MPEG-DASH, szyfrowanie AES-128-CTR.

### Pipeline

```
Input Video (dowolny format)
    │
    ├─> ffprobe (analiza: fps, rozdzielczość, długość)
    │
    ├─> ffmpeg (transkodowanie równoległe dla każdej jakości)
    │    ├─ H.264 (GPU: nvenc/amf/qsv)
    │    ├─ AAC audio
    │    └─ GOP alignment (keyframe co 4s)
    │
    ├─> Shaka Packager (segmentacja + szyfrowanie)
    │    ├─ MPEG-DASH (on-demand profile)
    │    ├─ 4s segmenty
    │    ├─ Separate init segments
    │    └─ AES-128-CTR (per-quality CEK)
    │
    └─> Generowanie metadanych
         ├─ metadata.json (ContentMetadata)
         ├─ encryption.json (CEK, KID)
         └─ thumbnail.jpg (10% długości)
```

### Parametry

```powershell
./prepare-content.ps1 `
    -InputFile "film.mp4" `
    -Qualities @('720p', '1080p') `
    -RequiredPlan "basic" `
    -Description "Opis filmu" `
    -ReleaseDate "2025-01-01"
```

| Parametr | Typ | Domyślnie | Opis |
|----------|-----|-----------|------|
| `-InputFile` | string | **wymagany** | Plik wejściowy |
| `-OutputDir` | string | `./content/storage` | Katalog wyjściowy |
| `-ContentId` | string | auto-UUID | Identyfikator contentu |
| `-Qualities` | array | `480p, 720p` | Rozdzielczości (`480p/720p/1080p/4k/all`) |
| `-RequiredPlan` | enum | `free` | Plan subskrypcji (`free/basic/pro`) |
| `-SkipEncryption` | switch | `false` | Pomija szyfrowanie |

### Profile transkodowania

| Jakość | Rozdzielczość | Video | Audio | H.264 Profile | GOP |
|--------|---------------|-------|-------|---------------|-----|
| **480p** | 854×480 | 1 Mbps | 128 kbps | baseline | fps×4 |
| **720p** | 1280×720 | 3 Mbps | 192 kbps | main | fps×4 |
| **1080p** | 1920×1080 | 5 Mbps | 256 kbps | high | fps×4 |
| **4K** | 3840×2160 | 15 Mbps | 320 kbps | high | fps×4 |

**GOP Alignment:**
```
GOP size = FPS × 4s (długość segmentu)
Przykład: 30 fps → GOP = 120 klatek
```

Gwarantuje, że keyframe pojawia się dokładnie na początku każdego segmentu MPEG-DASH.

### Wsparcie GPU

Wybierz kodek w zależności od GPU:

| GPU | Kodek ffmpeg | Wydajność |
|-----|--------------|-----------|
| **NVIDIA** | `h264_nvenc` | ~10x szybciej vs CPU |
| **AMD** | `h264_amf` | ~8x szybciej vs CPU |
| **Intel** | `h264_qsv` | ~6x szybciej vs CPU |
| **CPU** | `libx264` | Bazowa wydajność |

Domyślnie: `h264_nvenc` (linia 162 w skrypcie).

### Szyfrowanie (AES-128-CTR)

**Kluczowe cechy:**
- **Per-quality CEK** - każda jakość (480p, 720p, etc.) ma osobny klucz
- **Clear lead: 0s** - szyfrowanie od pierwszego segmentu
- **Random CEK** - 16 bajtów hex generowane losowo
- **UUID KID** - unikalny Key ID dla każdego CEK

**Struktura encryption.json:**
```json
{
  "ContentId": "uuid",
  "SegmentDuration": 4,
  "Qualities": {
    "480p": {
      "KeyId": "32-char-hex-kid",
      "KeyFile": "480p.key",
      "Algorithm": "AES-128-CTR"
    },
    "720p": { ... }
  }
}
```

**Plik `.key`:** Zawiera raw hex CEK (np. `a1b2c3d4e5f6...`) - **MUSI** być zapisany w bazie DRM Server z envelope encryption.

### Struktura wyjściowa

```
content/storage/{contentId}/
├── manifest.mpd              # Manifest MPEG-DASH
├── metadata.json             # Metadane (tytuł, długość, jakości, URL-e)
├── encryption.json           # Metadane szyfrowania (CEK, KID)
├── thumbnail.jpg             # Miniatura 480px (10% długości filmu)
│
├── 480p/
│   ├── 480p.key              # CEK dla 480p (16 bajtów hex)
│   ├── init_video.m4s        # Init segment video
│   ├── init_audio.m4s        # Init segment audio
│   ├── video_N.m4s           # Segmenty video (N = 1, 2, 3...)
│   └── audio_N.m4s           # Segmenty audio
│
├── 720p/
│   └── [analogicznie]
└── 1080p/
    └── [analogicznie]
```

### Wymagania

| Narzędzie | Minimalna wersja | Zastosowanie |
|-----------|------------------|--------------|
| **PowerShell** | 7.0+ | Wykonanie skryptu |
| **ffmpeg** | 4.4+ | Transkodowanie |
| **ffprobe** | 4.4+ | Analiza metadanych |
| **Shaka Packager** | 2.6+ | Segmentacja + szyfrowanie |

**Instalacja:** `./install-tools.ps1` (pobiera Shaka Packager do `./tools/`).

---

## 3. Biblioteka Shared

Współdzielona biblioteka .NET 9.0 z modelami danych, hierarchią wyjątków i stałymi systemowymi.

### Struktura

```
Shared/
├── Constants/
│   └── Plans.cs                          # free, basic, pro
├── Exceptions/
│   ├── NexaException.cs                  # Bazowa (abstract)
│   ├── ValidationException.cs            # 400
│   ├── NotFoundException.cs              # 404
│   ├── ServiceUnavailableException.cs    # 503
│   └── InternalServerException.cs        # 500
└── Models/
    ├── ErrorCode.cs                      # Stałe kodów błędów
    ├── ErrorResponse.cs                  # RFC 7807 Problem Details
    ├── ContentMetadata.cs                # Metadane filmu
    └── CatalogResponse.cs                # Odpowiedź /api/catalog
```

### Hierarchia wyjątków

```
Exception
  │
  └─ NexaException (abstract)
      ├─ ErrorCode: string          # np. "CONTENT_NOT_FOUND"
      ├─ StatusCode: int             # HTTP status (400/404/500/503)
      └─ Context: Dictionary         # Dodatkowe dane diagnostyczne
      │
      ├─ ValidationException         → 400 Bad Request
      ├─ NotFoundException           → 404 Not Found
      ├─ ServiceUnavailableException → 503 Service Unavailable
      └─ InternalServerException     → 500 Internal Server Error
```

**Użycie:**
```csharp
throw new NotFoundException(
    $"Film '{contentId}' nie został znaleziony.",
    new Dictionary<string, object> { ["contentId"] = contentId }
);
```

**Obsługa w middleware:**
```csharp
catch (NexaException nex)
{
    context.Response.StatusCode = nex.StatusCode;
    await context.Response.WriteAsJsonAsync(new ErrorResponse
    {
        ErrorCode = nex.ErrorCode,
        Message = nex.Message,
        Context = nex.Context,
        Timestamp = DateTime.UtcNow,
        Path = context.Request.Path
    });
}
```

### Modele danych

#### ContentMetadata

```csharp
public class ContentMetadata
{
    public string ContentId { get; set; }              // UUID
    public string Title { get; set; }
    public string Description { get; set; }
    public double DurationSeconds { get; set; }
    public string RequiredPlan { get; set; }           // free/basic/pro
    public List<string> AvailableQualities { get; set; }  // [480p, 720p, ...]
    public string ManifestUrl { get; set; }            // /content/{id}/manifest.mpd
    public string ThumbnailUrl { get; set; }           // /content/{id}/thumbnail.jpg
    public List<string>? Genres { get; set; }
    public DateTime? ReleaseDate { get; set; }
}
```

#### CatalogResponse

```csharp
public class CatalogResponse
{
    public int Total { get; set; }                     // Całkowita liczba filmów
    public int Limit { get; set; }                     // Żądany limit (max 50)
    public int Offset { get; set; }                    // Pozycja startowa
    public List<ContentMetadata> Items { get; set; }   // Contenty na stronie
}
```

#### ErrorResponse (RFC 7807)

```csharp
public class ErrorResponse
{
    public string ErrorCode { get; set; }              // CONTENT_NOT_FOUND
    public string Message { get; set; }                // Komunikat użytkownika
    public string? Details { get; set; }               // Stack trace (tylko dev)
    public DateTime Timestamp { get; set; }
    public string? Path { get; set; }                  // /api/catalog/invalid-id
    public Dictionary<string, object>? Context { get; set; }
}
```

### Kody błędów

| Kod | HTTP | Moduł |
|-----|------|-------|
| `VALIDATION_ERROR` | 400 | Ogólny |
| `NOT_FOUND` | 404 | Ogólny |
| `SERVICE_UNAVAILABLE` | 503 | Ogólny |
| `INTERNAL_SERVER_ERROR` | 500 | Ogólny |
| `CONTENT_NOT_FOUND` | 404 | Content Server |
| `MANIFEST_NOT_FOUND` | 404 | Content Server |
| `SEGMENT_NOT_FOUND` | 404 | Content Server |
| `THUMBNAIL_NOT_FOUND` | 404 | Content Server |
| `STORAGE_UNAVAILABLE` | 503 | Content Server |

### Stałe planów subskrypcji

```csharp
public static class Plans
{
    public const string FREE = "free";
    public const string BASIC = "basic";
    public const string PRO = "pro";

    public static bool IsValid(string plan) => plan is FREE or BASIC or PRO;
}
```

---

## Przepływ danych

```
┌──────────────────────┐
│  prepare-content.ps1 │  1. Przetwarza wideo
└──────────┬───────────┘     (transkodowanie, segmentacja, szyfrowanie)
           │
           ▼
┌─────────────────────────────────┐
│   Content Storage               │
│   /{contentId}/                 │
│   ├── manifest.mpd              │
│   ├── metadata.json ◄───────────┼─── 2. CatalogService indeksuje
│   ├── encryption.json           │        (FileSystemWatcher)
│   ├── thumbnail.jpg             │
│   └── {quality}/*.m4s           │
└─────────────────────────────────┘
           │
           ▼
┌────────────────────────────────────────┐
│       Content Server                   │
│                                        │
│  ┌──────────────────────────────────┐  │
│  │ Dwupoziomowy cache               │  │
│  │ L1: Output Cache (RAM, 100MB)    │  │
│  │ L2: Redis (TTL 3600s)            │  │
│  └──────────────────────────────────┘  │
│                                        │
│  ┌──────────────────────────────────┐  │
│  │ Lazy loading + Task.WhenAll      │  │
│  │ Paginacja na ID                  │  │
│  └──────────────────────────────────┘  │
│                                        │
│  Models: ContentMetadata,              │
│          CatalogResponse, ErrorResponse│
│  (z biblioteki Shared)                 │
└────────────────────────────────────────┘
           │
           │ 3. REST API + MPEG-DASH streaming
           ▼
┌──────────────────────┐
│  Aplikacja kliencka  │
│  (Web/Mobile)        │
└──────────────────────┘
```

---

## Podsumowanie techniczne

| Moduł | Technologia | Kluczowe mechanizmy |
|-------|-------------|---------------------|
| **Content Server** | ASP.NET Core 9.0 | Dwupoziomowy cache, lazy loading, path traversal protection, rate limiting (per-IP), CancellationToken, GPU-ready |
| **prepare-content.ps1** | PowerShell 7 + FFmpeg + Shaka | GOP alignment, per-quality CEK, AES-128-CTR, GPU acceleration (nvenc/amf/qsv), Task.WhenAll |
| **Shared** | .NET 9.0 Class Library | Hierarchia wyjątków z Context, RFC 7807, typed models, Plans validation |

### Zależności

```
prepare-content.ps1
       │
       │ Generuje
       ▼
┌─────────────────┐
│ Content Storage │  ◄──── FileSystemWatcher
└────────┬────────┘           (auto-invalidacja)
         │
         │ Czyta (lazy)
         ▼
┌──────────────────┐      Używa     ┌────────┐
│ Content Server   │ ◄───────────── │ Shared │
└──────────────────┘                └────────┘
         │
         │ Serwuje
         ▼
┌──────────────────┐
│  Klient (DASH)   │
└──────────────────┘
```
