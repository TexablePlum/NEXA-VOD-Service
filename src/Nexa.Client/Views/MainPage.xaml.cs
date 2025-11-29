using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexa.Client.Services.Auth;
using Nexa.Client.ViewModels;
using Nexa.Shared.Models;
using System;

namespace Nexa.Client.Views;

public sealed partial class MainPage : Page
{
    private readonly IAuthService _authService;
    private readonly TokenRefreshService _tokenRefreshService;
    private UserInfo? _currentUser;

    public MainPageViewModel ViewModel { get; }

    public MainPage()
    {
        this.InitializeComponent();

        _authService = App.Current.Services.GetRequiredService<IAuthService>();
        _tokenRefreshService = App.Current.Services.GetRequiredService<TokenRefreshService>();
        ViewModel = App.Current.Services.GetRequiredService<MainPageViewModel>();

        this.Loaded += MainPage_Loaded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Pobierz dane użytkownika przekazane z AuthPage
        if (e.Parameter is UserInfo userInfo)
        {
            _currentUser = userInfo;

            // Uruchom automatyczne odświeżanie tokenów w tle
            _tokenRefreshService.Start();

            // Załaduj katalog filmów
            await ViewModel.InitializeAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // Zatrzymaj automatyczne odświeżanie tokenów przy opuszczaniu strony
        _tokenRefreshService.Stop();
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Uruchom animacje tła
        AuroraAnimation.Begin();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        // Wyloguj użytkownika
        _authService.Logout();

        // Nawiguj z powrotem do AuthPage
        Frame.Navigate(typeof(AuthPage));
    }

    private void MovieGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ContentMetadata movie)
        {
            Frame.Navigate(typeof(PlayerPage), movie);
        }
    }

    private void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var scrollViewer = sender as ScrollViewer;
        if (scrollViewer == null) return;

        // Załaduj więcej filmów gdy użytkownik przewinie blisko końca
        var verticalOffset = scrollViewer.VerticalOffset;
        var maxVerticalOffset = scrollViewer.ScrollableHeight;

        if (maxVerticalOffset - verticalOffset < 500 && !ViewModel.IsLoading && ViewModel.CanLoadMore)
        {
            _ = ViewModel.LoadMoreCommand.ExecuteAsync(null);
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _ = ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private void Image_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image image)
        {
            // Ukryj obrazek, aby pokazać placeholder (FontIcon) pod spodem
            image.Visibility = Visibility.Collapsed;
        }
    }
}
