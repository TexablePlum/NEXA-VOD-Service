using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nexa.Client.Services.Auth;
using Nexa.Shared.Models;

namespace Nexa.Client.Views;

public sealed partial class MainPage : Page
{
    private readonly IAuthService _authService;
    private readonly TokenRefreshService _tokenRefreshService;
    private UserInfo? _currentUser;

    public MainPage()
    {
        this.InitializeComponent();

        _authService = App.Current.Services.GetRequiredService<IAuthService>();
        _tokenRefreshService = App.Current.Services.GetRequiredService<TokenRefreshService>();

        this.Loaded += MainPage_Loaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Pobierz dane użytkownika przekazane z AuthPage
        if (e.Parameter is UserInfo userInfo)
        {
            _currentUser = userInfo;
            UpdateUserInfo();

            // Uruchom automatyczne odświeżanie tokenów w tle
            _tokenRefreshService.Start();
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

    private void UpdateUserInfo()
    {
        if (_currentUser != null)
        {
            WelcomeText.Text = $"Witaj w NEXA!";
            UserEmailText.Text = _currentUser.Email;
            UserPlanText.Text = $"Plan: {_currentUser.Plan.ToUpper()}";
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        // Wyloguj użytkownika
        _authService.Logout();

        // Nawiguj z powrotem do AuthPage
        Frame.Navigate(typeof(AuthPage));
    }
}
