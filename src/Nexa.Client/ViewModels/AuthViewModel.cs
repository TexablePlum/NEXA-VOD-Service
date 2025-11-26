using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexa.Client.Services.Auth;
using Nexa.Client.Services.Exceptions;
using Nexa.Client.Services.Notifications;
using Nexa.Shared.Models;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Nexa.Client.ViewModels;

/// <summary>
/// ViewModel dla strony autoryzacji (Login/Register).
/// </summary>
public partial class AuthViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly INotificationService _notifications;

    // ==================== Properties ====================

    private string _email = string.Empty;
    public string Email
    {
        get => _email;
        set
        {
            SetProperty(ref _email, value);
            EmailError = null; // Wyczyść błąd przy edycji
        }
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set
        {
            SetProperty(ref _password, value);
            PasswordError = null;
        }
    }

    private string _confirmPassword = string.Empty;
    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            SetProperty(ref _confirmPassword, value);
            ConfirmPasswordError = null;
        }
    }

    private bool _rememberMe = true; // Domyślnie zaznaczone
    public bool RememberMe
    {
        get => _rememberMe;
        set => SetProperty(ref _rememberMe, value);
    }

    private bool _isLoginMode = true;
    public bool IsLoginMode
    {
        get => _isLoginMode;
        set
        {
            SetProperty(ref _isLoginMode, value);
            OnPropertyChanged(nameof(IsRegisterMode));
            OnPropertyChanged(nameof(ModeTitle));
            OnPropertyChanged(nameof(PrimaryButtonText));
            OnPropertyChanged(nameof(SecondaryPromptText));
            OnPropertyChanged(nameof(SecondaryActionText));
            ClearErrors();
        }
    }

    public bool IsRegisterMode => !_isLoginMode;

    public string ModeTitle => _isLoginMode ? "Zaloguj się" : "Utwórz konto";
    public string PrimaryButtonText => _isLoginMode ? "Zaloguj się" : "Zarejestruj się";
    public string SecondaryPromptText => _isLoginMode ? "Nie masz konta?" : "Masz już konto?";
    public string SecondaryActionText => _isLoginMode ? "Zarejestruj się" : "Zaloguj się";

    private bool _isLoading = false;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            SetProperty(ref _isLoading, value);
            OnPropertyChanged(nameof(IsFormEnabled));
        }
    }

    public bool IsFormEnabled => !_isLoading;

    // Błędy walidacji
    private string? _emailError;
    public string? EmailError
    {
        get => _emailError;
        set => SetProperty(ref _emailError, value);
    }

    private string? _passwordError;
    public string? PasswordError
    {
        get => _passwordError;
        set => SetProperty(ref _passwordError, value);
    }

    private string? _confirmPasswordError;
    public string? ConfirmPasswordError
    {
        get => _confirmPasswordError;
        set => SetProperty(ref _confirmPasswordError, value);
    }

    // ==================== Events ====================

    /// <summary>
    /// Event wywoływany po pomyślnym zalogowaniu/rejestracji.
    /// </summary>
    public event EventHandler<AuthResponse>? AuthenticationSucceeded;

    // ==================== Constructor ====================

    public AuthViewModel(IAuthService authService, INotificationService notifications)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    // ==================== Commands ====================

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (!ValidateLoginInputs())
            return;

        IsLoading = true;

        try
        {
            var response = await _authService.LoginAsync(Email, Password, RememberMe);

            _notifications.ShowSuccess($"Witaj, {response.User.Email}!", "Zalogowano pomyślnie");

            // Wyczyść hasło ze względów bezpieczeństwa
            Password = string.Empty;

            // Wywołaj event sukcesu
            AuthenticationSucceeded?.Invoke(this, response);
        }
        catch (NexaClientException ex)
        {
            HandleAuthenticationError(ex);
        }
        catch (Exception ex)
        {
            _notifications.ShowError($"Nieoczekiwany błąd: {ex.Message}", "Błąd");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (!ValidateRegisterInputs())
            return;

        IsLoading = true;

        try
        {
            var response = await _authService.RegisterAsync(Email, Password, plan: "free", rememberMe: RememberMe);

            _notifications.ShowSuccess($"Witaj w NEXA, {response.User.Email}!", "Rejestracja pomyślna");

            // Wyczyść hasła ze względów bezpieczeństwa
            Password = string.Empty;
            ConfirmPassword = string.Empty;

            // Wywołaj event sukcesu
            AuthenticationSucceeded?.Invoke(this, response);
        }
        catch (NexaClientException ex)
        {
            HandleAuthenticationError(ex);
        }
        catch (Exception ex)
        {
            _notifications.ShowError($"Nieoczekiwany błąd: {ex.Message}", "Błąd");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        IsLoginMode = !IsLoginMode;
        ClearInputs();
    }

    [RelayCommand]
    private void ForgotPassword()
    {
        // TODO: Implementacja w przyszłości
        _notifications.ShowInfo("Funkcja resetowania hasła będzie dostępna wkrótce.", "W przygotowaniu");
    }

    // ==================== Validation ====================

    private bool ValidateLoginInputs()
    {
        bool isValid = true;

        // Walidacja email
        if (string.IsNullOrWhiteSpace(Email))
        {
            EmailError = "Email jest wymagany";
            isValid = false;
        }
        else if (!IsValidEmail(Email))
        {
            EmailError = "Nieprawidłowy format email";
            isValid = false;
        }

        // Walidacja hasła
        if (string.IsNullOrWhiteSpace(Password))
        {
            PasswordError = "Hasło jest wymagane";
            isValid = false;
        }

        return isValid;
    }

    private bool ValidateRegisterInputs()
    {
        bool isValid = true;

        // Walidacja email
        if (string.IsNullOrWhiteSpace(Email))
        {
            EmailError = "Email jest wymagany";
            isValid = false;
        }
        else if (!IsValidEmail(Email))
        {
            EmailError = "Nieprawidłowy format email";
            isValid = false;
        }

        // Walidacja hasła
        if (string.IsNullOrWhiteSpace(Password))
        {
            PasswordError = "Hasło jest wymagane";
            isValid = false;
        }
        else if (Password.Length < 8)
        {
            PasswordError = "Hasło musi mieć minimum 8 znaków";
            isValid = false;
        }

        // Walidacja potwierdzenia hasła
        if (string.IsNullOrWhiteSpace(ConfirmPassword))
        {
            ConfirmPasswordError = "Potwierdź hasło";
            isValid = false;
        }
        else if (Password != ConfirmPassword)
        {
            ConfirmPasswordError = "Hasła nie są identyczne";
            isValid = false;
        }

        return isValid;
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // Prosty regex dla email (walidacja podstawowa)
        var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        return emailRegex.IsMatch(email);
    }

    // ==================== Error Handling ====================

    private void HandleAuthenticationError(NexaClientException ex)
    {
        // Obsługa konkretnych błędów
        switch (ex.Error.ErrorCode)
        {
            case "INVALID_CREDENTIALS":
                _notifications.ShowError("Nieprawidłowy email lub hasło.", "Błąd logowania");
                PasswordError = "Nieprawidłowe dane logowania";
                break;

            case "USER_ALREADY_EXISTS":
                _notifications.ShowError("Użytkownik z tym adresem email już istnieje.", "Błąd rejestracji");
                EmailError = "Ten email jest już zajęty";
                break;

            case "VALIDATION_ERROR":
                _notifications.ShowError(ex.Error.Message, "Błąd walidacji");
                // Pokaż szczegóły walidacji jeśli dostępne
                if (ex.Error.Details != null)
                {
                    EmailError = ex.Error.Details;
                }
                break;

            case "NETWORK_ERROR":
                _notifications.ShowError("Nie można połączyć się z serwerem. Sprawdź połączenie internetowe.", "Błąd sieci");
                break;

            case "FORBIDDEN":
                _notifications.ShowError("Twoje konto zostało dezaktywowane. Skontaktuj się z pomocą techniczną.", "Konto dezaktywowane");
                break;

            default:
                _notifications.ShowError(ex.Error.Message ?? "Wystąpił nieznany błąd.", "Błąd");
                break;
        }
    }

    // ==================== Helpers ====================

    private void ClearErrors()
    {
        EmailError = null;
        PasswordError = null;
        ConfirmPasswordError = null;
    }

    private void ClearInputs()
    {
        Email = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        ClearErrors();
    }
}
