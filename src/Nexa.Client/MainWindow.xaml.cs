using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nexa.Client.Views;

namespace Nexa.Client
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Rozszerza treœæ na pasek tytu³u
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Startuje od Splash Screena
            AppFrame.Navigate(typeof(SplashPage));
        }
    }
}