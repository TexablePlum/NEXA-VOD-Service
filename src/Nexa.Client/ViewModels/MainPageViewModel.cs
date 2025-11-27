using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexa.Client.Services.Auth;
using Nexa.Client.Services.Catalog;
using Nexa.Client.Services.Notifications;
using Nexa.Shared.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexa.Client.ViewModels
{
    public partial class MainPageViewModel : ObservableObject
    {
        private readonly ICatalogService _catalogService;
        private readonly ITokenManager _tokenManager;
        private readonly INotificationService _notificationService;
        private CancellationTokenSource? _loadCts;

        [ObservableProperty]
        private string _userEmail = string.Empty;

        [ObservableProperty]
        private string _userPlan = "Free";

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _hasError;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        public ObservableCollection<ContentMetadata> Movies { get; } = new();

        [ObservableProperty]
        private int _totalMovies;

        [ObservableProperty]
        private bool _canLoadMore;

        private int _currentOffset = 0;
        private const int PageSize = 20;

        public MainPageViewModel(
            ICatalogService catalogService,
            ITokenManager tokenManager,
            INotificationService notificationService)
        {
            _catalogService = catalogService;
            _tokenManager = tokenManager;
            _notificationService = notificationService;
        }

        public async Task InitializeAsync()
        {
            // Załaduj informacje o użytkowniku
            LoadUserInfo();

            // Załaduj katalog filmów
            await LoadCatalogAsync();
        }

        private void LoadUserInfo()
        {
            if (_tokenManager.HasSavedRefreshToken(out var email))
            {
                UserEmail = email ?? "Użytkownik";
            }
            else
            {
                UserEmail = "Użytkownik";
            }

            // TODO: Pobierz plan z tokenu JWT lub z osobnego endpointu
            UserPlan = "Free";
        }

        [RelayCommand]
        private async Task LoadCatalogAsync()
        {
            // Anuluj poprzednie ładowanie jeśli trwa
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();

            try
            {
                IsLoading = true;
                HasError = false;
                ErrorMessage = string.Empty;

                // Resetuj offset dla nowego wyszukiwania
                _currentOffset = 0;
                Movies.Clear();

                var response = await _catalogService.GetCatalogAsync(
                    limit: PageSize,
                    offset: _currentOffset,
                    search: string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery,
                    ct: _loadCts.Token
                );

                TotalMovies = response.Total;

                foreach (var movie in response.Items)
                {
                    Movies.Add(movie);
                }

                _currentOffset += response.Items.Count;
                CanLoadMore = _currentOffset < response.Total;
            }
            catch (OperationCanceledException)
            {
                // Ignoruj - użytkownik anulował
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = "Nie udało się załadować katalogu filmów. Sprawdź połączenie z internetem.";
                _notificationService.ShowError("Błąd ładowania", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task LoadMoreAsync()
        {
            if (!CanLoadMore || IsLoading) return;

            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();

            try
            {
                IsLoading = true;

                var response = await _catalogService.GetCatalogAsync(
                    limit: PageSize,
                    offset: _currentOffset,
                    search: string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery,
                    ct: _loadCts.Token
                );

                foreach (var movie in response.Items)
                {
                    Movies.Add(movie);
                }

                _currentOffset += response.Items.Count;
                CanLoadMore = _currentOffset < response.Total;
            }
            catch (OperationCanceledException)
            {
                // Ignoruj
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("Błąd ładowania", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task SearchAsync()
        {
            await LoadCatalogAsync();
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            SearchQuery = string.Empty;
            await LoadCatalogAsync();
        }

        [RelayCommand]
        private void SelectMovie(ContentMetadata movie)
        {
            if (movie == null) return;

            // TODO: Nawiguj do strony odtwarzacza
            _notificationService.ShowInfo("Film wybrany", $"Wybrano: {movie.Title}");
        }
    }
}
