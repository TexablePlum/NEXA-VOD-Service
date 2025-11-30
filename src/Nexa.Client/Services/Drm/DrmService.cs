using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Nexa.Client.Services.Auth;
using Nexa.Client.Services.Device;
using Nexa.Shared.Models;
using Windows.Storage;

namespace Nexa.Client.Services.Drm;

public class DrmService
{
    private readonly HttpClient _httpClient;
    private readonly ITokenManager _tokenManager;
    private readonly IDeviceRegistrationService _deviceService;
    private readonly Dictionary<string, Timer> _heartbeatTimers = new();
    private const string DeviceIdKey = "DeviceId";

    public DrmService(IHttpClientFactory httpClientFactory, ITokenManager tokenManager, IDeviceRegistrationService deviceService)
    {
        _httpClient = httpClientFactory.CreateClient("NexaGateway");
        _tokenManager = tokenManager;
        _deviceService = deviceService;
    }

    public async Task<List<QualityLicense>> GetLicensesAsync(string contentId)
    {
        var token = _tokenManager.GetAccessToken();
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("No access token available.");
        }

        var localSettings = ApplicationData.Current.LocalSettings;
        string? deviceId = localSettings.Values[DeviceIdKey] as string;

        if (string.IsNullOrEmpty(deviceId))
        {
             throw new InvalidOperationException("Device ID not found. Please register device.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/License/{contentId}?deviceId={deviceId}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        
        // Use ClientErrorHandler for better error messages
        if (!response.IsSuccessStatusCode)
        {
            var errorHandler = new ErrorHandling.ClientErrorHandler();
            await errorHandler.ThrowIfErrorAsync(response);
        }

        var licenseResponse = await response.Content.ReadFromJsonAsync<MultiQualityLicenseResponse>();
        
        if (licenseResponse == null)
        {
            throw new InvalidOperationException("Failed to deserialize license response.");
        }

        // Decrypt keys
        foreach (var license in licenseResponse.Licenses)
        {
            try 
            {
                byte[] encryptedKey = Convert.FromBase64String(license.EncryptedKey);
                byte[] decryptedKey = _deviceService.DecryptData(encryptedKey);
                
                // Replace encrypted key with clear key (hex string for Shaka Player)
                license.EncryptedKey = BitConverter.ToString(decryptedKey).Replace("-", "").ToLower();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to decrypt key for quality {license.Quality}: {ex.Message}");
                // We might want to remove this license from the list if we can't decrypt it
                // For now, we leave it but it won't work in player
            }
        }

        return licenseResponse.Licenses;
    }

    public void StartHeartbeat(string contentId)
    {
        if (_heartbeatTimers.ContainsKey(contentId))
        {
            return;
        }

        // Heartbeat every 30 seconds
        var timer = new Timer(async _ => await SendHeartbeatAsync(contentId), null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
        _heartbeatTimers[contentId] = timer;
    }

    public async Task StopHeartbeatAsync(string contentId)
    {
        if (_heartbeatTimers.TryGetValue(contentId, out var timer))
        {
            await timer.DisposeAsync();
            _heartbeatTimers.Remove(contentId);
        }

        await RevokeLicenseAsync(contentId);
    }

    private async Task SendHeartbeatAsync(string contentId)
    {
        try
        {
            var token = _tokenManager.GetAccessToken();
            if (string.IsNullOrEmpty(token)) return;

            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/License/{contentId}/heartbeat");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Heartbeat failed for {contentId}: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Heartbeat error for {contentId}: {ex.Message}");
        }
    }

    private async Task RevokeLicenseAsync(string contentId)
    {
        try
        {
            var token = _tokenManager.GetAccessToken();
            if (string.IsNullOrEmpty(token)) return;

            var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/License/{contentId}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            await _httpClient.SendAsync(request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Revoke license error for {contentId}: {ex.Message}");
        }
    }
}
