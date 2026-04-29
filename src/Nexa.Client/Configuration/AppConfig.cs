namespace Nexa.Client.Configuration
{
    public static class AppConfig
    {
        // Główny adres Gateway (Nginx). 
        public const string BaseApiUrl = "https://localhost/";

        // Timeouty
        public const int HealthCheckTimeoutSeconds = 3;
        public const int DefaultRequestTimeoutSeconds = 30;
    }
}