using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Nexa.Client.Services.Drm;
using Nexa.Shared.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace Nexa.Client.Views;

public sealed partial class PlayerPage : Page
{
    private readonly DrmService _drmService;
    private string _contentId = string.Empty;
    private string _manifestUrl = string.Empty;

    public PlayerPage()
    {
        this.InitializeComponent();
        var app = Application.Current as App;
        _drmService = app!.Services.GetService(typeof(DrmService)) as DrmService ?? throw new InvalidOperationException("DrmService not found");
        PlayerWebView.NavigationCompleted += PlayerWebView_NavigationCompleted;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ContentMetadata metadata)
        {
            _contentId = metadata.ContentId;
            // Assuming manifest URL is relative, construct full URL. 
            // In real app, BaseAddress should be configured.
            // For now assuming localhost or whatever the client is configured for.
            // But wait, WebView needs absolute URL.
            // We'll pass the relative URL to JS and let JS handle it if it knows the base, 
            // OR we construct it here.
            // Let's assume the API Gateway URL is known.
            // For this implementation, I'll pass the full URL if possible, or just the path.
            
            // Actually, let's look at how we call API. We use HttpClient factory "NexaGateway".
            // We can get the base address from there if needed, or just hardcode for dev.
            // Let's assume localhost for now or read from config if possible.
            // Better: Pass the full manifest URL to the player.
            
            // metadata.ManifestUrl is like "/content/{id}/manifest.mpd"
            // We need to prepend the server address.
            // Let's assume http://localhost:5000 for now (ContentServer/Gateway).
            // TODO: Get this from configuration.
            // Use configured BaseApiUrl from AppConfig
            string baseUrl = Nexa.Client.Configuration.AppConfig.BaseApiUrl.TrimEnd('/');
            _manifestUrl = $"{baseUrl}{metadata.ManifestUrl}";
            TitleTextBlock.Text = metadata.Title;

            await InitializePlayerAsync();
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (!string.IsNullOrEmpty(_contentId))
        {
            await _drmService.StopHeartbeatAsync(_contentId);
        }
    }

    private async Task InitializePlayerAsync()
    {
        try
        {
            // 1. Get Licenses (Keys)
            var licenses = await _drmService.GetLicensesAsync(_contentId);
            
            // 2. Start Heartbeat
            _drmService.StartHeartbeat(_contentId);

            // 3. Prepare Player Config
            var tokenManager = (Application.Current as App)!.Services.GetService(typeof(Nexa.Client.Services.Auth.ITokenManager)) as Nexa.Client.Services.Auth.ITokenManager;
            var accessToken = tokenManager?.GetAccessToken() ?? string.Empty;

            var config = new
            {
                manifestUrl = _manifestUrl,
                clearKeys = licenses.ToDictionary(l => l.KeyId, l => l.EncryptedKey), // EncryptedKey is now ClearKey (hex)
                accessToken = accessToken
            };

            var jsonConfig = JsonSerializer.Serialize(config);

            // 4. Load HTML
            // We need to map the Assets folder to a virtual host or load string.
            // SetVirtualHostNameToFolderMapping is cleaner.
            
            await PlayerWebView.EnsureCoreWebView2Async();
            PlayerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "nexa.player", 
                Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Player"), 
                CoreWebView2HostResourceAccessKind.Allow);

            PlayerWebView.Source = new Uri("https://nexa.player/player.html");

            // 5. Pass config to JS when ready
            // We'll do this in NavigationCompleted
            PlayerWebView.Tag = jsonConfig; // Store config temporarily
        }
        catch (Exception ex)
        {
            LoadingRing.IsActive = false;
            ShowError($"Failed to load player: {ex.Message}");
        }
    }

    private async void PlayerWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingRing.IsActive = false;
        if (args.IsSuccess && sender.Tag is string jsonConfig)
        {
            // Execute JS to init player
            await sender.ExecuteScriptAsync($"initPlayer({jsonConfig});");
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private async void ShowError(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
