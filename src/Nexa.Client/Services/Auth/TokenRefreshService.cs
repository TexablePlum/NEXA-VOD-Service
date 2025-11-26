using Microsoft.UI.Dispatching;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexa.Client.Services.Auth;

/// <summary>
/// Serwis automatycznego odświeżania tokenów w tle.
/// Sprawdza co minutę czy token wymaga odświeżenia i automatycznie go odświeża.
/// </summary>
public class TokenRefreshService : IDisposable
{
    private readonly IAuthService _authService;
    private readonly ITokenManager _tokenManager;
    private readonly DispatcherQueueTimer _timer;
    private bool _isRefreshing = false;

    public TokenRefreshService(IAuthService authService, ITokenManager tokenManager)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));

        // Utwórz timer który sprawdza token co 1 minutę
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += Timer_Tick;
    }

    /// <summary>
    /// Uruchamia automatyczne odświeżanie tokenów w tle.
    /// </summary>
    public void Start()
    {
        if (!_timer.IsRunning)
        {
            _timer.Start();
        }
    }

    /// <summary>
    /// Zatrzymuje automatyczne odświeżanie tokenów.
    /// </summary>
    public void Stop()
    {
        if (_timer.IsRunning)
        {
            _timer.Stop();
        }
    }

    private async void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        // Sprawdź czy użytkownik jest zalogowany
        if (!_authService.IsAuthenticated())
        {
            Stop();
            return;
        }

        // Zapobiegnij równoczesnym odświeżeniom
        if (_isRefreshing)
            return;

        try
        {
            _isRefreshing = true;

            // Sprawdź czy token wymaga odświeżenia (2 minuty przed wygaśnięciem)
            if (!_tokenManager.IsAccessTokenValid(bufferSeconds: 120))
            {
                // Token wygasł lub zaraz wygaśnie - odśwież go
                await _authService.RefreshTokenAsync();

#if DEBUG
                System.Diagnostics.Debug.WriteLine("[TokenRefreshService] Token automatycznie odświeżony");
#endif
            }
        }
        catch (Exception ex)
        {
            // Odświeżenie nie powiodło się - prawdopodobnie refresh token wygasł
            // Zatrzymaj timer, użytkownik będzie musiał się zalogować ponownie
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[TokenRefreshService] Błąd odświeżania tokenu: {ex.Message}");
#endif
            Stop();
            _authService.Logout();
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
