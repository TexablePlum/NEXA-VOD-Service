using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Nexa.Client.Configuration;
using Nexa.Client.Services.Auth;
using Nexa.Client.Services.Infrastructure;
using Nexa.Client.Services.Notifications;
using Nexa.Client.ViewModels;
using Nexa.Client.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Nexa.Client
{
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current;
        public IServiceProvider Services { get; }
        private Window? _window;

        public App()
        {
            this.InitializeComponent();
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // ViewModels
            services.AddTransient<SplashViewModel>();
            services.AddTransient<AuthViewModel>();

            // Views
            services.AddTransient<SplashPage>();
            services.AddTransient<AuthPage>();
            services.AddTransient<MainPage>();

            // Services

            // Konfiguracja HttpClient z użyciem AppConfig
            services.AddHttpClient("NexaGateway", client =>
            {
                client.BaseAddress = new Uri(AppConfig.BaseApiUrl);
                client.Timeout = TimeSpan.FromSeconds(AppConfig.DefaultRequestTimeoutSeconds);

                // Domyślne nagłówki
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            });

            // Serwis health check-ów
            services.AddSingleton<ISystemHealthService, SystemHealthService>();

            // Notyfikacje
            services.AddSingleton<INotificationService, NotificationService>();

            // Autoryzacja
            services.AddSingleton<ITokenManager, TokenManager>();
            services.AddSingleton<IAuthService>(sp =>
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient("NexaGateway");
                var tokenManager = sp.GetRequiredService<ITokenManager>();
                return new AuthService(httpClient, tokenManager);
            });
            services.AddSingleton<TokenRefreshService>();

            return services.BuildServiceProvider();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
