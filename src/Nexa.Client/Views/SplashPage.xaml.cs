using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Nexa.Client.ViewModels;

namespace Nexa.Client.Views
{
    public sealed partial class SplashPage : Page
    {
        public SplashViewModel ViewModel { get; }

        public SplashPage()
        {
            this.InitializeComponent();

            ViewModel = App.Current.Services.GetRequiredService<SplashViewModel>();

            this.Loaded += SplashPage_Loaded;
            ViewModel.InitializationCompleted += ViewModel_InitializationCompleted;
            ViewModel.AutoLoginSucceeded += ViewModel_AutoLoginSucceeded;
        }

        private void SplashPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            AuroraAnimation.Begin();
            LogoEntrance.Begin();
        }

        private void ViewModel_InitializationCompleted(object? sender, System.EventArgs e)
        {
            // Po udanej inicjalizacji przejdź do strony autoryzacji
            Frame.Navigate(typeof(AuthPage));
        }

        private void ViewModel_AutoLoginSucceeded(object? sender, Nexa.Shared.Models.UserInfo e)
        {
            // Auto-login się powiódł - przejdź od razu do MainPage
            Frame.Navigate(typeof(MainPage), e);
        }
    }
}