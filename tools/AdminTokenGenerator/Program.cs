using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// ========================================
// NEXA - Admin JWT Token Generator
// ========================================
// Generates admin JWT tokens for secure content upload operations
// Usage: dotnet run -- [jwt-secret] [expiry-hours]

Console.WriteLine("========================================");
Console.WriteLine("NEXA - Admin JWT Token Generator");
Console.WriteLine("========================================\n");

// Parse arguments
string? jwtSecret = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("JWT_SECRET");
int expiryHours = args.Length > 1 && int.TryParse(args[1], out var hours) ? hours : 24;

if (string.IsNullOrEmpty(jwtSecret))
{
    Console.WriteLine("✗ JWT_SECRET not provided");
    Console.WriteLine("\nUsage:");
    Console.WriteLine("  dotnet run -- <jwt-secret> [expiry-hours]");
    Console.WriteLine("  dotnet run (reads from JWT_SECRET env var)");
    Console.WriteLine("\nExample:");
    Console.WriteLine("  dotnet run -- \"my-super-secret-key\" 24");
    Console.WriteLine("  dotnet run -- \"my-super-secret-key\" 1  # 1 hour expiry");
    return 1;
}

if (jwtSecret.Length < 32)
{
    Console.WriteLine("✗ JWT_SECRET too short (minimum 32 characters for security)");
    Console.WriteLine($"  Current length: {jwtSecret.Length} characters");
    return 1;
}

try
{
    // Generate admin JWT token
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

    Console.WriteLine("✓ Admin JWT token generated successfully\n");
    Console.WriteLine($"Expires: {tokenDescriptor.Expires:yyyy-MM-dd HH:mm:ss} UTC ({expiryHours}h from now)");
    Console.WriteLine($"Issuer: {tokenDescriptor.Issuer}");
    Console.WriteLine($"Audience: {tokenDescriptor.Audience}");
    Console.WriteLine($"Role: admin");
    Console.WriteLine($"Scopes: content:upload, cek:import");
    Console.WriteLine("\n========================================");
    Console.WriteLine("TOKEN (copy this):");
    Console.WriteLine("========================================");
    Console.WriteLine(tokenString);
    Console.WriteLine("========================================\n");

    Console.WriteLine("Usage in PowerShell:");
    Console.WriteLine($"  $env:ADMIN_TOKEN = \"{tokenString}\"");
    Console.WriteLine("\nUsage in upload script:");
    Console.WriteLine("  -Headers @{ Authorization = \"Bearer $env:ADMIN_TOKEN\" }");

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Error generating token: {ex.Message}");
    return 1;
}
