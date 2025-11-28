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

        private string _userEmail = string.Empty;
        public string UserEmail
        {
            get => _userEmail;
            set => SetProperty(ref _userEmail, value);
        }

        private string _userPlan = "Free";
        public string UserPlan
        {
            get => _userPlan;
            set => SetProperty(ref _userPlan, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        public ObservableCollection<ContentMetadata> Movies { get; } = new();

        private int _totalMovies;
        public int TotalMovies
        {
            get => _totalMovies;
            set => SetProperty(ref _totalMovies, value);
        }

        private bool _canLoadMore;
        public bool CanLoadMore
        {
            get => _canLoadMore;
            set => SetProperty(ref _canLoadMore, value);
        }

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
