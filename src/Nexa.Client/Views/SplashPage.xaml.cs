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
        }

        private void SplashPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            AuroraAnimation.Begin();
            LogoEntrance.Begin();
        }
    }
}