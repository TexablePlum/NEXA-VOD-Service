using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexa.Client.Services.Auth;
using Nexa.Client.Services.Device;
using Nexa.Client.Services.Infrastructure;
using Nexa.Client.Services.Notifications;
using Nexa.Shared.Models;
using System;
using System.Threading.Tasks;

namespace Nexa.Client.ViewModels
{
    public partial class SplashViewModel : ObservableObject
    {
        private readonly ISystemHealthService _healthService;
        private readonly INotificationService _notifications;
        private readonly ITokenManager _tokenManager;
        private readonly IAuthService _authService;
        private readonly IDeviceRegistrationService _deviceRegistrationService;

        private string _loadingText = "Inicjalizacja...";
        public string LoadingText
        {
            get => _loadingText;
            set => SetProperty(ref _loadingText, value);
        }

        private bool _isRetryVisible = false;
        public bool IsRetryVisible
        {
            get => _isRetryVisible;
            set => SetProperty(ref _isRetryVisible, value);
        }

        private bool _isLoading = true;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        // Event informujący widok, że można iść do AuthPage
        public event EventHandler? InitializationCompleted;

        // Event informujący widok, że auto-login się powiódł i można iść do MainPage
        public event EventHandler<UserInfo>? AutoLoginSucceeded;

        public SplashViewModel(
            ISystemHealthService healthService,
            INotificationService notifications,
            ITokenManager tokenManager,
            IAuthService authService,
            IDeviceRegistrationService deviceRegistrationService)
        {
            _healthService = healthService;
            _notifications = notifications;
            _tokenManager = tokenManager;
            _authService = authService;
            _deviceRegistrationService = deviceRegistrationService;

            // Startuje proces od razu po utworzeniu VM
            _ = InitializeAsync();
        }

        [RelayCommand]
        private async Task Retry()
        {
            // Reset UI
            IsRetryVisible = false;
            IsLoading = true;
            await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                LoadingText = "Łączenie z NEXA Cloud...";
                await Task.Delay(1000); // Mały delay dla efektu wizualnego animacji

                // Sprawdzenie health check-a
                var healthStatus = await _healthService.CheckHealthAsync();

                if (healthStatus != SystemHealthStatus.Healthy)
                {
                    HandleError(healthStatus);
                    return;
                }

                LoadingText = "Weryfikacja DRM...";
                await Task.Delay(500); // TODO: Placeholder na przyszłą logikę device key

                // Sprawdź czy jest zapisany RefreshToken (Remember Me)
                if (_tokenManager.HasSavedRefreshToken(out var savedEmail))
                {
                    LoadingText = "Automatyczne logowanie...";
                    await Task.Delay(300);

                    var autoLoginSuccess = await TryAutoLoginAsync(savedEmail!);
                    if (autoLoginSuccess)
                    {
                        // Auto-login się powiódł - nie wywołujemy InitializationCompleted
                        return;
                    }
                    // Auto-login się nie powiódł - kontynuuj normalny flow do AuthPage
                }

                LoadingText = "Gotowe";
                await Task.Delay(200);

                // Sukces - odpala event do przejścia na AuthPage
                InitializationCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Nieoczekiwany błąd
                _notifications.ShowError($"Krytyczny błąd klienta: {ex.Message}");
                SetErrorState("Wystąpił błąd krytyczny.");
            }
        }

        private async Task<bool> TryAutoLoginAsync(string email)
        {
            try
            {
                // Spróbuj odświeżyć token z zapisanego RefreshToken
                var response = await _authService.RefreshTokenAsync();

                // Rejestracja urządzenia (jeśli wymagana)
                await _deviceRegistrationService.EnsureDeviceRegisteredAsync(response.User.UserId);

                // Sukces - wywołaj event AutoLoginSucceeded
                AutoLoginSucceeded?.Invoke(this, response.User);
                return true;
            }
            catch (Exception ex)
            {
                // Refresh się nie powiódł - token wygasł lub nieprawidłowy
                System.Diagnostics.Debug.WriteLine($"[SplashViewModel] Auto-login failed: {ex.Message}");

                // Auto-cleanup: Usuń niedziałający token z Credential Manager
                _tokenManager.RemoveSavedRefreshToken(email);
                _authService.Logout();

                return false;
            }
        }

        private void HandleError(SystemHealthStatus status)
        {
            switch (status)
            {
                case SystemHealthStatus.GatewayUnreachable:
                    _notifications.ShowError("Brak połączenia z bramą sieciową.", "Błąd sieci");
                    SetErrorState("Serwer nie odpowiada.");
                    break;

                case SystemHealthStatus.DrmUnreachable:
                    _notifications.ShowWarning("Usługa licencji DRM jest niedostępna.", "Błąd Serwera Licencji");
                    SetErrorState("Błąd systemu DRM.");
                    break;

                case SystemHealthStatus.ContentUnreachable:
                    _notifications.ShowWarning("Usługa katalogu filmów jest niedostępna.", "Błąd Serwera Treści");
                    SetErrorState("Błąd serwisu treści.");
                    break;
            }
        }

        private void SetErrorState(string userMessage)
        {
            LoadingText = userMessage;
            IsLoading = false;
            IsRetryVisible = true; // Pokazuje przycisk "Ponów"
        }
    }
}
