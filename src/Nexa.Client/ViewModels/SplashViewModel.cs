using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexa.Client.Services.Infrastructure;
using Nexa.Client.Services.Notifications;
using System;
using System.Threading.Tasks;

namespace Nexa.Client.ViewModels
{
    public partial class SplashViewModel : ObservableObject
    {
        private readonly ISystemHealthService _healthService;
        private readonly INotificationService _notifications;

        [ObservableProperty]
        private string _loadingText = "Inicjalizacja...";

        [ObservableProperty]
        private bool _isRetryVisible = false;

        [ObservableProperty]
        private bool _isLoading = true;

        // Event informujący widok, że można iść dalej
        public event EventHandler? InitializationCompleted;

        public SplashViewModel(ISystemHealthService healthService, INotificationService notifications)
        {
            _healthService = healthService;
            _notifications = notifications;

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

                LoadingText = "Gotowe";
                await Task.Delay(200);

                // Sukces - odpala event
                InitializationCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Nieoczekiwany błąd
                _notifications.ShowError($"Krytyczny błąd klienta: {ex.Message}");
                SetErrorState("Wystąpił błąd krytyczny.");
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
                    SetErrorState("Błąd systemu Content.");
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