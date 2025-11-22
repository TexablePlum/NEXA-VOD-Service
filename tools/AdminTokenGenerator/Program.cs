using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// ========================================
// NEXA - Admin JWT Token Generator
// ========================================
// Generuje token JWT dla konta administratora z uprawnieniami do przesyłania treści.
// Użycie: dotnet run -- [jwt-secret] [expiry-hours]

Console.WriteLine("========================================");
Console.WriteLine("NEXA - Admin JWT Token Generator");
Console.WriteLine("========================================\n");

// Pobierz sekret JWT i czas wygaśnięcia z argumentów lub zmiennych środowiskowych
string? jwtSecret = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("JWT_SECRET");
int expiryHours = args.Length > 1 && int.TryParse(args[1], out var hours) ? hours : 24;

if (string.IsNullOrEmpty(jwtSecret))
{
    Console.WriteLine("✗ JWT_SECRET nie został podany");
    Console.WriteLine("\nUżycie:");
    Console.WriteLine("  dotnet run -- <jwt-secret> [expiry-hours]");
    Console.WriteLine("  dotnet run (odczytuje z zmiennej środowiskowej JWT_SECRET)");
    Console.WriteLine("\nPrzykład:");
    Console.WriteLine("  dotnet run -- \"my-super-secret-key\" 24");
    Console.WriteLine("  dotnet run -- \"my-super-secret-key\" 1  # 1 hour expiry");
    return 1;
}

if (jwtSecret.Length < 32)
{
    Console.WriteLine("✗ JWT_SECRET jest za krótki (minimum 32 znaki dla bezpieczeństwa)");
    Console.WriteLine($"  Aktualna długość: {jwtSecret.Length} znaków");
    return 1;
}

try
{
    // Generuje token JWT dla konta administratora
    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.UTF8.GetBytes(jwtSecret);

    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "admin"),
            new Claim(ClaimTypes.Name, "ContentUploader"),
            new Claim("scope", "content:upload cek:import"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        }),
        Expires = DateTime.UtcNow.AddHours(expiryHours),
        Issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "NexaDrmServer",
        Audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "NexaClient",
        SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256Signature
        )
    };

    var token = tokenHandler.CreateToken(tokenDescriptor);
    var tokenString = tokenHandler.WriteToken(token);

    Console.WriteLine("✓ Token JWT administratora wygenerowany pomyślnie\n");
    Console.WriteLine($"Wygasa: {tokenDescriptor.Expires:yyyy-MM-dd HH:mm:ss} UTC ({expiryHours}h od teraz)");
    Console.WriteLine($"Wystawca: {tokenDescriptor.Issuer}");
    Console.WriteLine($"Odbiorca: {tokenDescriptor.Audience}");
    Console.WriteLine($"Rola: admin");
    Console.WriteLine($"Zakresy: content:upload, cek:import");
    Console.WriteLine("\n========================================");
    Console.WriteLine("TOKEN (kopiuj):");
    Console.WriteLine("========================================");
    Console.WriteLine(tokenString);
    Console.WriteLine("========================================\n");

    Console.WriteLine("Użycie w PowerShell:");
    Console.WriteLine($"  $env:ADMIN_TOKEN = \"{tokenString}\"");
    Console.WriteLine("\nUżycie w skrypcie przesyłania:");
    Console.WriteLine("  -Headers @{ Authorization = \"Bearer $env:ADMIN_TOKEN\" }");

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Błąd podczas generowania tokenu: {ex.Message}");
    return 1;
}
