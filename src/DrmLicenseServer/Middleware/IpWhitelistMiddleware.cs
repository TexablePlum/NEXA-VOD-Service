using System.Net;

namespace Nexa.DrmLicenseServer.Middleware;

/// <summary>
/// Middleware do whitelisty IP – ogranicza dostêp do endpointów administracyjnych
/// Pozwala na ¿¹dania tylko z localhost lub wewnêtrznej sieci Docker
/// </summary>
public class IpWhitelistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpWhitelistMiddleware> _logger;
    private readonly HashSet<string> _whitelistedPaths;
    private readonly List<IPNetwork> _allowedNetworks;

    public IpWhitelistMiddleware(
        RequestDelegate next,
        ILogger<IpWhitelistMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        _whitelistedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "/api/admin/cek/import"
        };

        _allowedNetworks = new List<IPNetwork>
        {
            // Localhost (IPv4)
            IPNetwork.Parse("127.0.0.0/8"),

            // Localhost (IPv6)
            IPNetwork.Parse("::1/128"),

            IPNetwork.Parse("172.16.0.0/12"),  // 172.16.0.0 - 172.31.255.255

            IPNetwork.Parse("172.18.0.0/16"),  // Common Docker Compose range

            // Sieci prywatne (RFC 1918) – bezpieczne, poniewa¿ endpoint jest za Nginx
            // Wymagane, gdy host uzyskuje dostêp przez localhost za poœrednictwem Nginx
            IPNetwork.Parse("10.0.0.0/8"),      // 10.0.0.0 - 10.255.255.255
            IPNetwork.Parse("192.168.0.0/16"),  // 192.168.0.0 - 192.168.255.255
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        var requiresWhitelist = _whitelistedPaths.Any(wp =>
            path.StartsWith(wp, StringComparison.OrdinalIgnoreCase));

        if (requiresWhitelist)
        {
            var remoteIp = context.Connection.RemoteIpAddress;

            if (remoteIp == null || !IsIpAllowed(remoteIp))
            {
                _logger.LogWarning(
                    "SECURITY: Blocked request to {Path} from unauthorized IP: {RemoteIp}",
                    path,
                    remoteIp?.ToString() ?? "unknown"
                );

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Forbidden",
                    message = "Access denied: IP address not whitelisted for admin endpoints"
                });
                return;
            }

            _logger.LogInformation(
                "SECURITY: Allowed request to {Path} from IP: {RemoteIp}",
                path,
                remoteIp?.ToString() ?? "unknown"
            );
        }

        await _next(context);
    }

    private bool IsIpAllowed(IPAddress ipAddress)
    {
        // Konwertuje IPv6 na IPv4
        if (ipAddress.IsIPv4MappedToIPv6)
        {
            ipAddress = ipAddress.MapToIPv4();
        }

        // Sprawdza, czy IP nale¿y do którejkolwiek z dozwolonych sieci
        return _allowedNetworks.Any(network => network.Contains(ipAddress));
    }
}

/// <summary>
/// Klasa pomocnicza do dopasowywania sieci IP (notacja CIDR)
/// </summary>
public class IPNetwork
{
    private readonly IPAddress _network;
    private readonly IPAddress _mask;

    private IPNetwork(IPAddress network, IPAddress mask)
    {
        _network = network;
        _mask = mask;
    }

    public static IPNetwork Parse(string cidr)
    {
        var parts = cidr.Split('/');
        var ip = IPAddress.Parse(parts[0]);
        var prefixLength = int.Parse(parts[1]);

        var maskBytes = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? new byte[4]
            : new byte[16];

        for (int i = 0; i < prefixLength; i++)
        {
            maskBytes[i / 8] |= (byte)(128 >> (i % 8));
        }

        var mask = new IPAddress(maskBytes);

        var ipBytes = ip.GetAddressBytes();
        for (int i = 0; i < ipBytes.Length; i++)
        {
            ipBytes[i] &= maskBytes[i];
        }

        var network = new IPAddress(ipBytes);

        return new IPNetwork(network, mask);
    }

    public bool Contains(IPAddress address)
    {
        if (address.AddressFamily != _network.AddressFamily)
            return false;

        var addressBytes = address.GetAddressBytes();
        var networkBytes = _network.GetAddressBytes();
        var maskBytes = _mask.GetAddressBytes();

        for (int i = 0; i < addressBytes.Length; i++)
        {
            if ((addressBytes[i] & maskBytes[i]) != networkBytes[i])
                return false;
        }

        return true;
    }
}
