using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;

namespace Wattzicht.App.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly AuthService _auth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmailError))]
    private string? _emailError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPasswordError))]
    private string? _passwordError;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isPasswordHidden = true;

    public bool HasEmailError => !string.IsNullOrEmpty(EmailError);
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);

    public LoginViewModel(AuthService auth)
    {
        _auth = auth;
        Title = "Inloggen";
    }

    partial void OnEmailChanged(string value) => EmailError = null;
    partial void OnPasswordChanged(string value) => PasswordError = null;

    [RelayCommand]
    private void TogglePasswordVisibility() => IsPasswordHidden = !IsPasswordHidden;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (!Validate()) return;

        await RunSafeAsync(async () =>
        {
            try
            {
                await _auth.LoginAsync(Email, Password);
                Password = string.Empty;
                await Shell.Current.GoToAsync(AppShell.Routes.Home);
            }
            catch (ApiException ex) when (ex.IsUnauthorized || ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                PasswordError = "E-mail of wachtwoord klopt niet.";
            }
        });
    }

    [RelayCommand]
    private Task NoAccountAsync() =>
        ShowAlertAsync(
            "Nog geen account?",
            "Je installateur maakt je account aan en stuurt je de inloggegevens. Neem contact op met je installateur.");

    private bool Validate()
    {
        EmailError = null;
        PasswordError = null;

        if (string.IsNullOrWhiteSpace(Email))
            EmailError = "Vul je e-mailadres in.";
        else if (!EmailRegex.IsMatch(Email.Trim()))
            EmailError = "Dit is geen geldig e-mailadres.";

        if (string.IsNullOrEmpty(Password))
            PasswordError = "Vul je wachtwoord in.";

        return !HasEmailError && !HasPasswordError;
    }
}
