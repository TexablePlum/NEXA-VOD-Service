using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nexa.Client.ViewModels;

namespace Nexa.Client.Views;

public sealed partial class AuthPage : Page
{
    public AuthViewModel ViewModel { get; }

    public AuthPage()
    {
        this.InitializeComponent();

        ViewModel = App.Current.Services.GetRequiredService<AuthViewModel>();

        this.Loaded += AuthPage_Loaded;
        ViewModel.AuthenticationSucceeded += ViewModel_AuthenticationSucceeded;
    }

    private void AuthPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Uruchom animacje tła
        AuroraAnimation.Begin();
        ContentEntrance.Begin();

        // Focus na pole Email
        EmailTextBox.Focus(FocusState.Programmatic);
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        // Dynamiczne wywołanie Login lub Register w zależności od trybu
        if (ViewModel.IsLoginMode)
        {
            await ViewModel.LoginCommand.ExecuteAsync(null);
        }
        else
        {
            await ViewModel.RegisterCommand.ExecuteAsync(null);
        }
    }

    private void ViewModel_AuthenticationSucceeded(object? sender, Nexa.Shared.Models.AuthResponse e)
    {
        // Po pomyślnym zalogowaniu/rejestracji przejdź do MainPage
        // Przekaż dane użytkownika jako parametr nawigacji
        Frame.Navigate(typeof(MainPage), e.User);
    }
}
