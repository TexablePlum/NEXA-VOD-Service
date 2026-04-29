using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Nexa.Client.Services.Drm;
using Nexa.Shared.Models;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;

namespace Nexa.Client.Views;

public sealed partial class PlayerPage : Page
{
    private readonly DrmService _drmService;
    private readonly Services.Auth.IAuthService _authService;
    private readonly Services.Notifications.INotificationService _notificationService;
    private string _contentId = string.Empty;
    private string _manifestUrl = string.Empty;
    private string _thumbnailUrl = string.Empty;
    private bool _isPageUnloaded = false;

    public PlayerPage()
    {
        this.InitializeComponent();
        var app = Application.Current as App;
        _drmService = app!.Services.GetService(typeof(DrmService)) as DrmService ?? throw new InvalidOperationException("DrmService not found");
        _authService = app!.Services.GetService(typeof(Services.Auth.IAuthService)) as Services.Auth.IAuthService ?? throw new InvalidOperationException("AuthService not found");
        _notificationService = app!.Services.GetService(typeof(Services.Notifications.INotificationService)) as Services.Notifications.INotificationService ?? throw new InvalidOperationException("NotificationService not found");
        
        PlayerWebView.NavigationCompleted += PlayerWebView_NavigationCompleted;
        PlayerWebView.CoreWebView2Initialized += PlayerWebView_CoreWebView2Initialized;

        Loaded += PlayerPage_Loaded;
        Unloaded += PlayerPage_Unloaded;
    }

    private void PlayerPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow != null)
        {
            App.MainWindow.Closed += MainWindow_Closed;
        }
    }

    private void PlayerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow != null)
        {
            App.MainWindow.Closed -= MainWindow_Closed;
        }
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // Try to release license on app close (Alt+F4)
        if (!string.IsNullOrEmpty(_contentId))
        {
            // Fire and forget, hoping it goes through before process kill
            await _drmService.StopHeartbeatAsync(_contentId);
        }
    }

    private void PlayerWebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (args.Exception != null)
        {
            _notificationService.ShowError($"WebView2 Initialization Failed: {args.Exception.Message}", "Critical Error");
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isPageUnloaded = false;

        if (e.Parameter is ContentMetadata metadata)
        {
            _contentId = metadata.ContentId;
            _thumbnailUrl = metadata.ThumbnailUrl;
            
            string baseUrl = Nexa.Client.Configuration.AppConfig.BaseApiUrl.TrimEnd('/');
            _manifestUrl = $"{baseUrl}{metadata.ManifestUrl}";
            TitleTextBlock.Text = metadata.Title;

            await InitializePlayerAsync();
        }
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _isPageUnloaded = true;
        
        // Stop playback and destroy player
        try 
        {
            if (PlayerWebView.CoreWebView2 != null)
            {
                await PlayerWebView.ExecuteScriptAsync("destroyPlayer();");
                
                // Navigate to blank page to stop all activity without destroying the control
                // This allows the WebView to be reused on next navigation
                PlayerWebView.CoreWebView2.NavigateToString("<html><body style='background:black'></body></html>");
            }
        }
        catch { /* Ignore if WebView is already gone */ }

        // DON'T call PlayerWebView.Close() - it permanently destroys the instance
        // and causes "CoreWebView2 is null" errors on subsequent navigations

        if (!string.IsNullOrEmpty(_contentId))
        {
            await _drmService.StopHeartbeatAsync(_contentId);
        }
    }

    private async Task InitializePlayerAsync()
    {
        try
        {
            // 0. Ensure WebView2 is in a valid state
            if (PlayerWebView.CoreWebView2 == null)
            {
                System.Diagnostics.Debug.WriteLine("PlayerPage: CoreWebView2 is null, initializing...");
                var envOptions = new CoreWebView2EnvironmentOptions();
                envOptions.AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required";
                var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, null, envOptions);
                await PlayerWebView.EnsureCoreWebView2Async(env);
            }

            if (_isPageUnloaded) return; // Abort if page was closed during async call

            if (PlayerWebView.CoreWebView2 == null)
            {
                throw new InvalidOperationException("Failed to initialize WebView2 - CoreWebView2 is still null after EnsureCoreWebView2Async. The WebView may be in a corrupted state.");
            }

            System.Diagnostics.Debug.WriteLine($"PlayerPage: Initializing player for content: {_contentId}");

            // 1. Get Licenses (Keys)
            var licenses = await _drmService.GetLicensesAsync(_contentId);
            
            if (_isPageUnloaded) return; // Abort if page was closed during async call

            // Calculate Max Height based on available licenses
            int maxHeight = 0;
            foreach (var license in licenses)
            {
                int height = ParseQualityToHeight(license.Quality);
                if (height > maxHeight) maxHeight = height;
            }

            // Fallback if no valid quality found (should not happen if licenses exist)
            if (maxHeight == 0) maxHeight = 576; // Default to SD

            // 2. Start Heartbeat
            _drmService.StartHeartbeat(_contentId);

            // 3. Prepare Player Config - Get valid access token (auto-refreshes if expired)
            var accessToken = await _authService.GetValidAccessTokenAsync();
            
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new InvalidOperationException("No access token available. User must log in again.");
            }

            // thumbnailUrl and _manifestUrl are already absolute URLs from the service
            string thumbnailUrl = _thumbnailUrl;
            string manifestUrl = _manifestUrl;

            var config = new
            {
                manifestUrl = _manifestUrl,
                posterUrl = thumbnailUrl,
                clearKeys = licenses.ToDictionary(l => l.KeyId, l => l.EncryptedKey),
                accessToken,
                maxHeight // Pass max height to JS
            };

            var jsonConfig = JsonSerializer.Serialize(config);

            // 4. Setup WebView2 virtual host mapping
            // Environment variables are set at app startup in App.xaml.cs
            // WebView2 is already initialized and validated above
            
            if (_isPageUnloaded) return; // Abort if page was closed during init

            if (PlayerWebView.CoreWebView2 != null)
            {
                PlayerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "nexa.player", 
                    Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "Player"), 
                    CoreWebView2HostResourceAccessKind.Allow);

                // Security, disabled all browser actions.
                PlayerWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                PlayerWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                PlayerWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

                // Listen for messages from JS
                PlayerWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                
                // Listen for Fullscreen changes
                PlayerWebView.CoreWebView2.ContainsFullScreenElementChanged += CoreWebView2_ContainsFullScreenElementChanged;

                // 5. Store config and Navigate
                PlayerWebView.Tag = jsonConfig; // Store config for handshake

                PlayerWebView.CoreWebView2.Navigate("https://nexa.player/player.html");
                
                // Start auto-hide timer
                StartAutoHideTimer();
            }
            else
            {
                // This can happen if initialization "succeeded" but the control is in a weird state
                // Usually handled by EnsureCoreWebView2Async throwing, but just in case
                throw new InvalidOperationException("WebView2 initialized but CoreWebView2 is null.");
            }
        }
        catch (Services.Exceptions.NexaClientException ex) when (ex.StatusCode == 403)
        {
            LoadingRing.IsActive = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            
            // Use Context dictionary to distinguish between different 403 scenarios
            if (ex.Error.Context != null && ex.Error.Context.ContainsKey("limit") && ex.Error.Context.ContainsKey("activeStreams"))
            {
                // Concurrent stream limit exceeded - has limit and activeStreams in context
                ShowError("Przekroczono limit aktywnych odtworzeń. Zamknij inne sesje aby móc tu odtwarzać.");
            }
            else if (ex.Error.Context != null && ex.Error.Context.ContainsKey("requiredPlan"))
            {
                // Insufficient plan - has requiredPlan in context
                string requiredPlan = ex.Error.Context["requiredPlan"]?.ToString() ?? "wyższy";
                ShowError($"Nie masz dostępu do tego filmu. Wymagany plan: {requiredPlan}.");
            }
            else if (ex.Error.Context != null && ex.Error.Context.ContainsKey("releaseDate"))
            {
                // Content not yet released - has releaseDate in context
                ShowError("Ten film nie został jeszcze opublikowany. Wróć po dacie premiery.");
            }
            else
            {
                // Generic forbidden error - use the message from backend
                ShowError(ex.Error.Message ?? "Brak dostępu do tego contentu.");
            }
        }
        catch (Services.Exceptions.NexaClientException ex) when (ex.StatusCode == 429)
        {
            LoadingRing.IsActive = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            
            // Rate limit exceeded - extract retry time from context
            int retryAfter = 60; // default
            if (ex.Error.Context != null && ex.Error.Context.ContainsKey("retryAfter"))
            {
                if (ex.Error.Context["retryAfter"] is int retry)
                {
                    retryAfter = retry;
                }
                else if (int.TryParse(ex.Error.Context["retryAfter"]?.ToString(), out int parsedRetry))
                {
                    retryAfter = parsedRetry;
                }
            }
            
            string message = retryAfter > 60 
                ? $"Wysłano zbyt wiele żądań. Spróbuj ponownie za {retryAfter / 60} minut."
                : $"Wysłano zbyt wiele żądań. Spróbuj ponownie za {retryAfter} sekund.";
            
            ShowError(message);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            LoadingRing.IsActive = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            
            // Fallback for HttpRequestException (if NexaClientException wasn't caught)
            // Parse the error message to distinguish between different 403 scenarios
            string errorMsg = ex.Message;
            
            if (errorMsg.Contains("jednoczesnych streamów") || errorMsg.Contains("concurrent"))
            {
                // Concurrent stream limit exceeded
                ShowError("Osiągnięto limit jednoczesnych odtworzeń. Jeśli zamknąłeś aplikację niestandardowo (np. Alt+F4), odczekaj do 2 minut na wygaśnięcie sesji.");
            }
            else if (errorMsg.Contains("plan") && (errorMsg.Contains("wymagany") || errorMsg.Contains("required")))
            {
                // Insufficient plan - user doesn't have access to this content
                ShowError("Nie masz dostępu do tego filmu. Twój plan subskrypcji nie pozwala na odtworzenie tego contentu. Zmień plan, aby uzyskać dostęp.");
            }
            else if (errorMsg.Contains("nie został jeszcze wypuszczony") || errorMsg.Contains("not yet released"))
            {
                // Content not yet released
                ShowError("Ten film nie został jeszcze opublikowany. Wróć po dacie premiery.");
            }
            else
            {
                // Generic forbidden error
                ShowError($"Brak dostępu: {errorMsg}");
            }
        }
        catch (Exception ex)
        {
            if (_isPageUnloaded) return; // Ignore errors if we are leaving anyway

            LoadingRing.IsActive = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            ShowError($"Failed to load player: {ex.Message}");
        }
    }

    private int ParseQualityToHeight(string quality)
    {
        // Expected formats: "480p", "720p", "1080p", "1440p", "2160p"
        if (string.IsNullOrEmpty(quality)) return 0;
        
        string numberPart = quality.ToLower().Replace("p", "");
        if (int.TryParse(numberPart, out int height))
        {
            return height;
        }
        return 0;
    }

    private DispatcherTimer? _autoHideTimer;
    private bool _isControlsVisible = true;

    private void StartAutoHideTimer()
    {
        _autoHideTimer = new DispatcherTimer();
        _autoHideTimer.Interval = TimeSpan.FromSeconds(3.5); // Increased to match Shaka's fade out better
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
        if (message == "playerReady")
        {
            // JS is ready, send config
            if (PlayerWebView.Tag is string jsonConfig)
            {
                // Send config as a JSON message
                var msg = new { type = "initConfig", config = JsonSerializer.Deserialize<object>(jsonConfig) };
                var msgJson = JsonSerializer.Serialize(msg);
                PlayerWebView.CoreWebView2.PostWebMessageAsJson(msgJson);
            }
        }
        else if (message == "playbackStarted")
        {
            // Hide loading overlay when video actually starts playing
            LoadingRing.IsActive = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        else if (message == "userActive")
        {
            ShowControls();
        }
        else if (message.StartsWith("error:"))
        {
            // Handle error from JS
            string errorMsg = message.Substring(6);
            ShowError(errorMsg);
            LoadingRing.IsActive = false;
        }
    }

    private void CoreWebView2_ContainsFullScreenElementChanged(CoreWebView2 sender, object args)
    {
        var appWindow = GetAppWindowForCurrentWindow();
        if (sender.ContainsFullScreenElement)
        {
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        }
        else
        {
            appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            ControlsGrid.Visibility = Visibility.Visible;
        }
    }

    private Microsoft.UI.Windowing.AppWindow GetAppWindowForCurrentWindow()
    {
        if (App.MainWindow == null) throw new InvalidOperationException("MainWindow is null");
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
    }

    private void PlayerWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            _notificationService.ShowError($"Navigation Failed: {args.WebErrorStatus}", "Player Error");
            return;
        }
        // Config is now injected via AddScriptToExecuteOnDocumentCreatedAsync
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void ShowError(string message)
    {
        // Use NotificationService instead of ContentDialog
        _notificationService.ShowError(message, "Błąd Odtwarzacza");
    }
}
