using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;

namespace Wattzicht.App.ViewModels;

public partial class LoginViewModel : BaseViewModel
{
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly AuthService _auth;
    private readonly ApiClient _api;
    private readonly ISocialeLogin _sociaal;

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

    // ---- Aanmelden ----
    //
    // Tot 12-11-2026 kon je je in de app niet aanmelden: het inlogscherm wees je door naar je
    // installateur. Dat paste bij het white-labelverhaal, maar niet bij wat Wattzicht is: iedereen
    // moet zijn apparaten kunnen koppelen, met of zonder installateur. Wie wél begeleiding wil, vult
    // de code van zijn installateur in; dat is de uitzondering, niet de deur.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titel))]
    [NotifyPropertyChangedFor(nameof(KnopTekst))]
    [NotifyPropertyChangedFor(nameof(WisselTekst))]
    private bool _isRegistreren;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHouseholdNameError))]
    private string? _householdNameError;

    [ObservableProperty]
    private string _householdName = string.Empty;

    [ObservableProperty]
    private string _installerCode = string.Empty;

    public bool HasHouseholdNameError => !string.IsNullOrEmpty(HouseholdNameError);

    public string Titel => IsRegistreren ? "Account aanmaken" : "Inloggen";
    public string KnopTekst => IsRegistreren ? "Account aanmaken" : "Inloggen";
    public string WisselTekst => IsRegistreren
        ? "Heb je al een account? Inloggen"
        : "Nog geen account? Maak er een aan";

    public bool HasEmailError => !string.IsNullOrEmpty(EmailError);
    public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);

    public LoginViewModel(AuthService auth, ApiClient api, ISocialeLogin sociaal)
    {
        _auth = auth;
        _api = api;
        _sociaal = sociaal;
        Title = "Inloggen";
    }

    /// <summary>
    /// Welke knoppen er mogen staan. De server bepaalt dat, want daar staan de sleutels; het toestel
    /// bepaalt het tweede deel, want zonder uitvoering voor dit platform doet een knop niets.
    /// </summary>
    public async Task LaadProvidersAsync()
    {
        var beschikbaar = await _api.ProvidersAsync();
        ToonGoogle = beschikbaar.Contains("google") && _sociaal.Ondersteunt("google");
        ToonApple = beschikbaar.Contains("apple") && _sociaal.Ondersteunt("apple");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToonSociaal))]
    private bool _toonGoogle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToonSociaal))]
    private bool _toonApple;

    /// <summary>Is er iets te tonen? Zo niet, dan blijft ook het streepje met "of" weg.</summary>
    public bool ToonSociaal => ToonGoogle || ToonApple;

    [RelayCommand]
    private async Task SociaalAsync(string provider)
    {
        ErrorMessage = null;

        await RunSafeAsync(async () =>
        {
            var token = await _sociaal.TokenAsync(provider);
            if (string.IsNullOrWhiteSpace(token))
            {
                // Weggeklikt, of de aanmelding werd geweigerd. In het eerste geval hoort er niets te
                // gebeuren; in het tweede kan de gebruiker er niets aan doen en helpt alleen de
                // gewone weg.
                return;
            }

            try
            {
                await _auth.ExternalLoginAsync(provider, token);
                await Shell.Current.GoToAsync(AppShell.Routes.Home);
            }
            catch (ApiException ex) when (ex.IsUnauthorized)
            {
                ErrorMessage = "Dat account is niet geaccepteerd. Probeer het opnieuw, of meld je aan met je e-mailadres.";
            }
        });
    }

    partial void OnEmailChanged(string value) => EmailError = null;
    partial void OnPasswordChanged(string value) => PasswordError = null;
    partial void OnHouseholdNameChanged(string value) => HouseholdNameError = null;

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

    /// <summary>Wisselen tussen inloggen en aanmelden, zonder van scherm te wisselen.</summary>
    [RelayCommand]
    private void ToggleRegistreren()
    {
        IsRegistreren = !IsRegistreren;
        EmailError = PasswordError = HouseholdNameError = null;
        ErrorMessage = null;
        Title = Titel;
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (!Validate()) return;

        await RunSafeAsync(async () =>
        {
            try
            {
                await _auth.RegisterAsync(Email, Password, HouseholdName, InstallerCode);
                Password = string.Empty;
                await Shell.Current.GoToAsync(AppShell.Routes.Home);
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Op dit adres bestaat al een account. Dat is geen fout van de server maar van het
                // scherm waar je op staat, dus zetten we hem meteen op inloggen.
                EmailError = "Op dit adres bestaat al een account. Log in met je wachtwoord.";
                IsRegistreren = false;
                Title = Titel;
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest
                                          || ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                // De server keurt bijvoorbeeld een code zonder bestaande installateur af. Zijn
                // melding is bruikbaarder dan een eigen verzinsel.
                ErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Aanmelden lukte niet. Controleer je gegevens."
                    : ex.Message;
            }
        });
    }

    private bool Validate()
    {
        EmailError = null;
        PasswordError = null;

        HouseholdNameError = null;

        if (IsRegistreren)
        {
            if (string.IsNullOrWhiteSpace(HouseholdName))
                HouseholdNameError = "Geef je huishouden een naam, bijvoorbeeld Thuis.";
            // Acht tekens is wat de server eist; dat hier ook zeggen scheelt een mislukte poging.
            if (!string.IsNullOrEmpty(Password) && Password.Length < 8)
                PasswordError = "Kies een wachtwoord van minimaal 8 tekens.";
        }

        if (string.IsNullOrWhiteSpace(Email))
            EmailError = "Vul je e-mailadres in.";
        else if (!EmailRegex.IsMatch(Email.Trim()))
            EmailError = "Dit is geen geldig e-mailadres.";

        if (string.IsNullOrEmpty(Password))
            PasswordError = "Vul je wachtwoord in.";

        return !HasEmailError && !HasPasswordError && !HasHouseholdNameError;
    }
}
