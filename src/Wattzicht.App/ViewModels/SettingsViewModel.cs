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

    [RelayCommand]
    public async Task LoadAsync()
    {
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
