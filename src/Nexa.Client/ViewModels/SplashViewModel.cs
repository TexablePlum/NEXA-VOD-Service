using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;

namespace Nexa.Client.ViewModels
{
    public partial class SplashViewModel : ObservableObject
    {
        private string _loadingText = "Inicjalizacja...";

        public string LoadingText
        {
            get => _loadingText;
            set => SetProperty(ref _loadingText, value);
        }

        public SplashViewModel()
        {
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await Task.Delay(1000);
            LoadingText = "Łączenie z NEXA Cloud...";

            await Task.Delay(1000);
            LoadingText = "Weryfikacja DRM...";

            await Task.Delay(500);
            LoadingText = "Gotowe";
        }
    }
}