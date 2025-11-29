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
    private string _thumbnailUrl = string.Empty;

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
            _thumbnailUrl = metadata.ThumbnailUrl;
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
        
        // Stop playback and destroy player
        try 
        {
            await PlayerWebView.ExecuteScriptAsync("destroyPlayer();");
        }
        catch { /* Ignore if WebView is already gone */ }

        // Navigate to blank to ensure media resources are released
        PlayerWebView.Source = new Uri("about:blank");

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

            // Construct full thumbnail URL
            string baseUrl = Nexa.Client.Configuration.AppConfig.BaseApiUrl.TrimEnd('/');
            string thumbnailUrl = !string.IsNullOrEmpty(_thumbnailUrl) ? $"{baseUrl}{_thumbnailUrl}" : "";

            var config = new
            {
                manifestUrl = _manifestUrl,
                posterUrl = thumbnailUrl,
                clearKeys = licenses.ToDictionary(l => l.KeyId, l => l.EncryptedKey), // EncryptedKey is now ClearKey (hex)
                accessToken = accessToken
            };

            var jsonConfig = JsonSerializer.Serialize(config);

            // 4. Load HTML
            // 4. Load HTML
            // Initialize WebView2 with options to allow mixed content and disable web security (for dev)
            // Workaround for CreateAsync signature mismatch: set env var
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--allow-running-insecure-content --disable-web-security");
            
            await PlayerWebView.EnsureCoreWebView2Async();

            PlayerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "nexa.player", 
                Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Player"), 
                CoreWebView2HostResourceAccessKind.Allow);

            // Listen for messages from JS
            PlayerWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            
            // Listen for Fullscreen changes
            PlayerWebView.CoreWebView2.ContainsFullScreenElementChanged += CoreWebView2_ContainsFullScreenElementChanged;

            PlayerWebView.Source = new Uri("https://nexa.player/player.html");

            // 5. Pass config to JS when ready
            // We'll do this in NavigationCompleted
            PlayerWebView.Tag = jsonConfig; // Store config temporarily
            
            // Start auto-hide timer
            StartAutoHideTimer();
        }
        catch (Exception ex)
        {
            LoadingRing.IsActive = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            ShowError($"Failed to load player: {ex.Message}");
        }
    }

    private DispatcherTimer? _autoHideTimer;
    private bool _isControlsVisible = true;

    private void StartAutoHideTimer()
    {
        _autoHideTimer = new DispatcherTimer();
        _autoHideTimer.Interval = TimeSpan.FromSeconds(3.2); // Increased to match Shaka's fade out better
        _autoHideTimer.Tick += (s, e) =>
        {
            HideControls();
            _autoHideTimer.Stop();
        };
        _autoHideTimer.Start();
    }

    private void ShowControls()
    {
        if (!_isControlsVisible)
        {
            _isControlsVisible = true;
            ShowControlsStoryboard.Begin();
        }
        _autoHideTimer?.Stop();
        _autoHideTimer?.Start();
    }

    private void HideControls()
    {
        if (_isControlsVisible)
        {
            _isControlsVisible = false;
            HideControlsStoryboard.Begin();
        }
    }

    private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var message = args.TryGetWebMessageAsString();
        if (message == "playbackStarted")
        {
            // Hide loading overlay when video actually starts playing
            LoadingRing.IsActive = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        else if (message == "userActive")
        {
            ShowControls();
        }
    }

    private void CoreWebView2_ContainsFullScreenElementChanged(CoreWebView2 sender, object args)
    {
        var appWindow = GetAppWindowForCurrentWindow();
        if (sender.ContainsFullScreenElement)
        {
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            // We want our custom controls to be visible in fullscreen too, controlled by JS events.
            // ControlsGrid.Visibility = Visibility.Collapsed; // REMOVED
        }
        else
        {
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            ControlsGrid.Visibility = Visibility.Visible;
        }
    }

    private Microsoft.UI.Windowing.AppWindow GetAppWindowForCurrentWindow()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
    }

    private async void PlayerWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        // Don't hide loading ring here yet, wait for playbackStarted
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
