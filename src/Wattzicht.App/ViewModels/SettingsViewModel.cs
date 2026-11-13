using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    private readonly ApiClient _api;
    private readonly AuthService _auth;

    // ---- Tarief ----
    [ObservableProperty] private string _importTariff = "0,30";
    [ObservableProperty] private string _exportTariff = "0,08";
    [ObservableProperty] private string _feedInCost = "0,00";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasFeedInCostError))] private string? _feedInCostError;
    public bool HasFeedInCostError => !string.IsNullOrWhiteSpace(FeedInCostError);
    [ObservableProperty] private bool _isDynamic;
    [ObservableProperty] private bool _isSavingTariff;
    [ObservableProperty] private string? _tariffSavedText;

    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasImportTariffError))] private string? _importTariffError;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasExportTariffError))] private string? _exportTariffError;
    public bool HasImportTariffError => !string.IsNullOrEmpty(ImportTariffError);
    public bool HasExportTariffError => !string.IsNullOrEmpty(ExportTariffError);

    // ---- Weergave ----
    //
    // Donker is de standaard, net als op de site. Geen "volg mijn telefoon": dan ziet de ene
    // bewoner een andere app dan de andere en kan een eigen keuze het systeem niet overrulen.
    public bool IsDonkerThema
    {
        get => ThemaVoorkeur.IsDonker;
        set
        {
            if (ThemaVoorkeur.IsDonker == value) return;
            ThemaVoorkeur.IsDonker = value;
            OnPropertyChanged();
        }
    }

    // ---- Installateur ----
    [ObservableProperty] private InstallerBrandingDto? _installer;
    [ObservableProperty] private string _installerName = "Je installateur";
    [ObservableProperty] private string? _installerPhone;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HasInstallerPhone))] private bool _hasPhone;
    [ObservableProperty] private bool _hasQuoteUrl;
    [ObservableProperty] private string? _installerLogo;
    [ObservableProperty] private bool _hasInstallerLogo;

    /// <summary>Mag de installateur meekijken? Wijzigen slaat direct op.</summary>
    [ObservableProperty] private bool _installerAccess;
    [ObservableProperty] private string? _lastViewedText;
    private bool _syncingAccess;
    public bool HasInstallerPhone => HasPhone;

    // ---- Account ----
    public string HouseholdName => _auth.Household?.Name ?? "";
    public string VersionText => $"Wattzicht {AppConfig.Version} ({AppConfig.Build})";
    public string ApiText => AppConfig.ApiBaseUrl;

    private readonly PushService _push;

    // Pushmeldingen
    [ObservableProperty] private bool _notifyAlerts = true;
    [ObservableProperty] private bool _notifySurplus = true;
    private bool _syncingNotify;

    public SettingsViewModel(ApiClient api, AuthService auth, PushService push)
    {
        _api = api;
        _auth = auth;
        _push = push;
        Title = "Instellingen";
    }

    partial void OnImportTariffChanged(string value) { ImportTariffError = null; TariffSavedText = null; }
    partial void OnExportTariffChanged(string value) { ExportTariffError = null; TariffSavedText = null; }
    partial void OnIsDynamicChanged(bool value) => TariffSavedText = null;

    // ---- Account verwijderen ----
    //
    // Weghalen moet kunnen vanuit de app zelf, en het moet echt weg zijn. Daarom twee keer vragen:
    // één keer wat het betekent, en daarna nog een keer of je het zeker weet. Niet om te pesten,
    // maar omdat dit het enige is in de app dat je niet terug kunt draaien.
    [RelayCommand]
    private async Task AccountVerwijderenAsync()
    {
        var zeker = await Shell.Current.DisplayAlert(
            "Account verwijderen",
            "Je huishouden, je gekoppelde apparaten en al je meetgegevens worden verwijderd. "
            + "Dit kan niet ongedaan gemaakt worden.",
            "Verder", "Annuleren");
        if (!zeker) return;

        var echt = await Shell.Current.DisplayAlert(
            "Zeker weten?",
            "Hierna is alles weg en kun je niet meer inloggen met dit account.",
            "Ja, verwijder alles", "Nee, laat staan");
        if (!echt) return;

        await RunSafeAsync(async () =>
        {
            await _api.VerwijderHuishoudenAsync();
            await _auth.LogoutAsync();
            await Shell.Current.GoToAsync("//login");
        });
    }

    // ---- LaadCash ----
    //
    // Wattzicht en LaadCash zijn allebei van DoganTech, en wie thuis laadt laat geld liggen zolang
    // hij zijn ERE's niet laat inboeken. De server bepaalt wat er te tonen valt: wie al klant is
    // krijgt geen aanbieding meer, en wie "niet meer tonen" koos hoort er nooit meer iets over.
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HeeftLaadcash))] private LaadcashPromoDto? _laadcash;

    public bool HeeftLaadcash => Laadcash is { Show: true };
    public bool LaadcashGekoppeld => Laadcash is { Linked: true };
    public bool LaadcashNogNiet => Laadcash is { Show: true, Linked: false };

    public string LaadcashTitel => Laadcash?.Title ?? "Verdien geld met je laadpaal";
    public string LaadcashTekst => Laadcash?.Body ?? "";

    [RelayCommand]
    private async Task LaadcashOpenenAsync()
    {
        if (Laadcash?.DeepLink is not { Length: > 0 } link) return;

        // Eerst vastleggen dat hij geklikt heeft, dan pas openen. Andersom is de app weg voordat het
        // verzoek de deur uit is, en dan weten we niet wat er werkt.
        try { await _api.LaadcashActieAsync("tile", "clicked"); } catch (Exception) { }
        await Launcher.Default.OpenAsync(link);
    }

    public async Task LaadcashLadenAsync()
    {
        try { Laadcash = await _api.LaadcashPromoAsync(); }
        catch (Exception) { Laadcash = null; }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await LaadcashLadenAsync();

        await RunSafeAsync(async () =>
        {
            // Tarief en installateur onafhankelijk ophalen; een 404 op één van beide mag de rest niet blokkeren.
            try
            {
                var tariff = await _api.GetTariffAsync();
                ImportTariff = tariff.ImportTariffEur.ToString("0.00", Nl);
                ExportTariff = tariff.ExportTariffEur.ToString("0.00", Nl);
                FeedInCost = (tariff.FeedInCostEur ?? 0m).ToString("0.000", Nl);
                IsDynamic = tariff.IsDynamic;
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }

            try
            {
                Installer = await _api.GetInstallerAsync();
                InstallerName = string.IsNullOrWhiteSpace(Installer.Name) ? "Je installateur" : Installer.Name;
                InstallerPhone = Installer.Phone;
                HasPhone = !string.IsNullOrWhiteSpace(Installer.Phone);
                HasQuoteUrl = Uri.TryCreate(Installer.QuoteUrl, UriKind.Absolute, out _);
                InstallerLogo = Installer.LogoDataUrl;
                HasInstallerLogo = !string.IsNullOrWhiteSpace(Installer.LogoDataUrl);
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }

            try
            {
                var access = await _api.GetInstallerAccessAsync();
                _syncingAccess = true;
                InstallerAccess = access.Granted;
                _syncingAccess = false;

                var log = await _api.GetAccessLogAsync();
                LastViewedText = log.Count == 0
                    ? "Nog niet bekeken door je installateur"
                    : $"Laatst bekeken: {log[0].At.ToLocalTime().ToString("d MMM HH:mm", Nl)}";
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
            finally { _syncingAccess = false; }

            try
            {
                var notify = await _api.GetNotificationSettingsAsync();
                _syncingNotify = true;
                NotifyAlerts = notify.Alerts;
                NotifySurplus = notify.Surplus;
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
            finally { _syncingNotify = false; }

            OnPropertyChanged(nameof(HouseholdName));
        }, _auth);
    }

    partial void OnNotifyAlertsChanged(bool value) => SaveNotifySettings();
    partial void OnNotifySurplusChanged(bool value) => SaveNotifySettings();

    private async void SaveNotifySettings()
    {
        if (_syncingNotify) return;
        try
        {
            await _api.SetNotificationSettingsAsync(new NotificationSettingsDto(NotifyAlerts, NotifySurplus));
            // Iemand die meldingen aanzet, wil ze ook ontvangen: toestel (opnieuw) aanmelden en toestemming vragen.
            if (NotifyAlerts || NotifySurplus)
                await _push.RegisterAsync();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    partial void OnInstallerAccessChanged(bool value)
    {
        if (_syncingAccess) return;
        _ = SaveInstallerAccessAsync(value);
    }

    private async Task SaveInstallerAccessAsync(bool granted)
    {
        try
        {
            var saved = await _api.SetInstallerAccessAsync(granted);
            if (saved.Granted != InstallerAccess)
            {
                _syncingAccess = true;
                InstallerAccess = saved.Granted;
                _syncingAccess = false;
            }
        }
        catch (ApiException ex)
        {
            // Terugzetten zodat de schakelaar de echte stand toont.
            _syncingAccess = true;
            InstallerAccess = !granted;
            _syncingAccess = false;
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveTariffAsync()
    {
        ImportTariffError = ExportTariffError = FeedInCostError = null;

        if (!TryParseEur(ImportTariff, out var import) || import <= 0 || import > 2)
            ImportTariffError = "Vul een tarief in tussen € 0,01 en € 2,00 per kWh.";
        if (!TryParseEur(ExportTariff, out var export) || export < 0 || export > 2)
            ExportTariffError = "Vul een vergoeding in tussen € 0,00 en € 2,00 per kWh.";
        if (!TryParseEur(FeedInCost, out var feedIn) || feedIn < 0 || feedIn > 1)
            FeedInCostError = "Vul terugleverkosten in tussen € 0,00 en € 1,00 per kWh.";
        if (HasImportTariffError || HasExportTariffError || HasFeedInCostError) return;

        IsSavingTariff = true;
        try
        {
            var saved = await _api.UpdateTariffAsync(new TariffDto(import, export, IsDynamic, DateOnly.FromDateTime(DateTime.Today), FeedInCostEur: feedIn));
            ImportTariff = saved.ImportTariffEur.ToString("0.00", Nl);
            ExportTariff = saved.ExportTariffEur.ToString("0.00", Nl);
            FeedInCost = (saved.FeedInCostEur ?? 0m).ToString("0.000", Nl);
            TariffSavedText = "Opgeslagen. Je besparing wordt vanaf vandaag met dit tarief berekend.";
        }
        catch (ApiException ex)
        {
            ImportTariffError = ex.Message;
            if (ex.IsUnauthorized) await _auth.LogoutAsync();
        }
        finally
        {
            IsSavingTariff = false;
        }
    }

    [RelayCommand]
    private async Task CallInstallerAsync()
    {
        if (string.IsNullOrWhiteSpace(InstallerPhone)) return;
        try
        {
            if (PhoneDialer.Default.IsSupported)
                PhoneDialer.Default.Open(InstallerPhone);
            else
                await Clipboard.Default.SetTextAsync(InstallerPhone);
        }
        catch
        {
            await ShowAlertAsync("Bellen lukt niet", $"Bel zelf: {InstallerPhone}");
        }
    }

    [RelayCommand]
    private async Task RequestQuoteAsync()
    {
        var url = Installer?.QuoteUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await ShowAlertAsync("Offerte", $"Neem contact op met {InstallerName} voor een offerte voor een thuisbatterij.");
            return;
        }
        try
        {
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch
        {
            await ShowAlertAsync("Openen lukt niet", "De offertepagina kon niet worden geopend.");
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        var confirmed = await ConfirmAsync("Uitloggen?", "Je kunt daarna opnieuw inloggen met je e-mail en wachtwoord.", "Uitloggen");
        if (!confirmed) return;
        await _auth.LogoutAsync(); // meldt ook het toestel af voor push
    }

    private static bool TryParseEur(string? text, out decimal value)
    {
        var cleaned = (text ?? "").Replace("€", "").Trim().Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
