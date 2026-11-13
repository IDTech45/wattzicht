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

    // ---- Taal ----
    /// <summary>De drie talen, in de taal zelf.</summary>
    public IReadOnlyList<TaalKeuze> Talen { get; } = Taal.Keuzes;

    /// <summary>
    /// De gekozen taal. Zetten past de hele app meteen aan: elk label in de XAML hangt aan
    /// <see cref="Taal.Nu"/> en ververst zichzelf, dus er is geen herstart nodig.
    /// </summary>
    public TaalKeuze GekozenTaal
    {
        get => Talen.FirstOrDefault(t => t.Code == Taal.Code) ?? Talen[0];
        set
        {
            if (value is null || value.Code == Taal.Code) return;
            Taal.Zet(value.Code);
            OnPropertyChanged();

            // De teksten die uit de viewmodel komen (de LaadCash-tegel komt van de server) hangen
            // niet aan de indexer; die halen we opnieuw op.
            _ = LoadAsync();
        }
    }

    // ---- Je woning ----
    [ObservableProperty] private string _woningNaam = string.Empty;
    [ObservableProperty] private string _plaats = string.Empty;
    [ObservableProperty] private string _piekVermogen = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HeeftWoningFout))] private string? _woningFout;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HeeftWoningBewaard))] private string? _woningBewaard;

    /// <summary>
    /// Of we de plaats hebben kunnen vinden. Zonder plaats geen zonverwachting, en dat is precies
    /// wat hier moet staan in plaats van alleen "geen verwachting beschikbaar" op het adviesscherm.
    /// </summary>
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ZonStatus))] private bool _heeftLocatie;

    public bool HeeftWoningFout => !string.IsNullOrEmpty(WoningFout);
    public bool HeeftWoningBewaard => !string.IsNullOrEmpty(WoningBewaard);

    public string ZonStatus => Taal.T(HeeftLocatie ? "woning.zon-aan" : "woning.zon-uit");

    /// <summary>
    /// Plaats en piekvermogen opslaan.
    ///
    /// De plaats is hier het belangrijkste veld: zonder plaats hebben we geen coordinaten, zonder
    /// coordinaten geen zonverwachting, en dan staat er op het adviesscherm "vul je postcode in"
    /// terwijl er nergens een postcodeveld was. Het piekvermogen maakt van die verwachting een getal
    /// in kilowatturen voor dit dak in plaats van voor een gemiddeld dak.
    /// </summary>
    [RelayCommand]
    private async Task WoningOpslaanAsync()
    {
        WoningFout = null;
        WoningBewaard = null;

        double? kwp = null;
        var ingevuld = PiekVermogen.Trim().Replace('.', ',');
        if (!string.IsNullOrWhiteSpace(ingevuld))
        {
            if (!double.TryParse(ingevuld, System.Globalization.NumberStyles.Float, Nl, out var waarde)
                || waarde <= 0 || waarde > 100)
            {
                WoningFout = Taal.T("woning.piek-fout");
                return;
            }

            kwp = waarde;
        }

        await RunSafeAsync(async () =>
        {
            var woning = await _api.WoningBijwerkenAsync(new UpdateHouseholdRequest(
                Name: string.IsNullOrWhiteSpace(WoningNaam) ? null : WoningNaam.Trim(),
                PostalCode: string.IsNullOrWhiteSpace(Plaats) ? null : Plaats.Trim(),
                City: string.IsNullOrWhiteSpace(Plaats) ? null : Plaats.Trim(),
                PvPeakKw: kwp));

            HeeftLocatie = woning.HasLocation;
            WoningBewaard = Taal.T(woning.HasLocation ? "woning.bewaard" : "woning.plaats-niet-gevonden");
            _auth.ZetHuishouden(woning);
            OnPropertyChanged(nameof(HouseholdName));
        }, _auth);
    }

    /// <summary>De velden van de woning vullen met wat we al weten.</summary>
    private void VulWoningVelden()
    {
        var woning = _auth.Household;
        if (woning is null) return;

        WoningNaam = woning.Name;
        // Plaats of postcode: één veld voor de bewoner, want voor het opzoeken van coordinaten maakt
        // het niet uit en twee velden voor hetzelfde doel is een vraag te veel.
        Plaats = !string.IsNullOrWhiteSpace(woning.City) ? woning.City! : woning.PostalCode ?? string.Empty;
        PiekVermogen = woning.PvPeakKw is { } kwp ? kwp.ToString("0.##", Nl) : string.Empty;
        HeeftLocatie = woning.HasLocation;
    }

    // ---- Historie uit het portaal van de fabrikant ----
    [ObservableProperty][NotifyPropertyChangedFor(nameof(HeeftImportResultaat))] private string? _importResultaat;

    public bool HeeftImportResultaat => !string.IsNullOrEmpty(ImportResultaat);

    /// <summary>
    /// Een export uit het portaal van de fabrikant inlezen.
    ///
    /// Waarom dit in de app staat en niet alleen op de site: het bestand komt bij Enphase per mail
    /// binnen, en die mail staat op de telefoon. Hem daar openen en meteen uploaden is één stap;
    /// eerst naar een computer lopen is er een te veel.
    /// </summary>
    [RelayCommand]
    private async Task HistorieImporterenAsync()
    {
        ImportResultaat = null;

        try
        {
            var keuze = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = Taal.T("historie.kies-bestand"),
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    // Android kent geen bestandsextensies maar MIME-typen, en een CSV uit een
                    // mailbijlage komt daar regelmatig binnen als text/plain of als octet-stream.
                    [DevicePlatform.Android] = ["text/csv", "text/comma-separated-values", "text/plain", "application/vnd.ms-excel", "application/octet-stream"],
                    [DevicePlatform.iOS] = ["public.comma-separated-values-text", "public.plain-text"],
                    [DevicePlatform.WinUI] = [".csv", ".txt"],
                }),
            });

            if (keuze is null) return;

            IsBusy = true;
            await using var stroom = await keuze.OpenReadAsync();
            var uit = await _api.ImporteerHistorieAsync(stroom, keuze.FileName);

            ImportResultaat = string.Format(
                Taal.T("historie.gelukt"),
                uit.Dagen,
                uit.Van.ToString("d MMM yyyy", Nl),
                uit.Tot.ToString("d MMM yyyy", Nl),
                uit.TotaalKwh.ToString("0", Nl));

            if (uit.Overgeslagen > 0)
            {
                ImportResultaat += " " + string.Format(Taal.T("historie.overgeslagen"), uit.Overgeslagen);
            }

            // Eerlijk erbij: een ingelezen dag heeft alleen opwek. Wat iemand die dag zelf gebruikte
            // of terugleverde weten we niet, dus blijft de besparing van die dagen leeg. Dat is
            // beter dan een aanname als bedrag neerzetten, maar je moet het wel weten.
            ImportResultaat += " " + Taal.T("historie.alleen-opwek");
        }
        catch (ApiException ex)
        {
            // De server vertelt precies wat er niet klopte, inclusief welke kolommen hij zag staan.
            // Dat is hier de hele winst: "ongeldig bestand" helpt niemand.
            ImportResultaat = ex.Message;
        }
        catch (Exception)
        {
            ImportResultaat = Taal.T("historie.mislukt");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---- Installateur ----
    [ObservableProperty] private InstallerBrandingDto? _installer;
    [ObservableProperty] private string _installerName = string.Empty;
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
        Title = Taal.T("meer.instellingen");
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
            Taal.T("meer.account-verwijderen"),
            Taal.T("meer.verwijderen-waarschuwing"),
            Taal.T("algemeen.verder"), Taal.T("algemeen.annuleren"));
        if (!zeker) return;

        var echt = await Shell.Current.DisplayAlert(
            Taal.T("meer.zeker-weten"),
            Taal.T("meer.verwijderen-definitief"),
            Taal.T("meer.ja-verwijder-alles"), Taal.T("meer.nee-laat-staan"));
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

    public string LaadcashTitel => Laadcash?.Title ?? Taal.T("meer.verdien-geld-met-je-laadpaal");
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
        VulWoningVelden();
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
                InstallerName = string.IsNullOrWhiteSpace(Installer.Name) ? Taal.T("meer.je-installateur") : Installer.Name;
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
                    ? Taal.T("meer.nog-niet-bekeken")
                    : string.Format(Taal.T("meer.laatst-bekeken"), log[0].At.ToLocalTime().ToString("d MMM HH:mm", Nl));
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
            ImportTariffError = Taal.T("meer.tarief-tussen");
        if (!TryParseEur(ExportTariff, out var export) || export < 0 || export > 2)
            ExportTariffError = Taal.T("meer.vergoeding-tussen");
        if (!TryParseEur(FeedInCost, out var feedIn) || feedIn < 0 || feedIn > 1)
            FeedInCostError = Taal.T("meer.terugleverkosten-tussen");
        if (HasImportTariffError || HasExportTariffError || HasFeedInCostError) return;

        IsSavingTariff = true;
        try
        {
            var saved = await _api.UpdateTariffAsync(new TariffDto(import, export, IsDynamic, DateOnly.FromDateTime(DateTime.Today), FeedInCostEur: feedIn));
            ImportTariff = saved.ImportTariffEur.ToString("0.00", Nl);
            ExportTariff = saved.ExportTariffEur.ToString("0.00", Nl);
            FeedInCost = (saved.FeedInCostEur ?? 0m).ToString("0.000", Nl);
            TariffSavedText = Taal.T("meer.tarief-opgeslagen");
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
            await ShowAlertAsync(Taal.T("meer.bellen-lukt-niet"), string.Format(Taal.T("meer.bel-zelf"), InstallerPhone));
        }
    }

    [RelayCommand]
    private async Task RequestQuoteAsync()
    {
        var url = Installer?.QuoteUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            await ShowAlertAsync(Taal.T("meer.offerte"), string.Format(Taal.T("meer.neem-contact-op-voor-offerte"), InstallerName));
            return;
        }
        try
        {
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch
        {
            await ShowAlertAsync(Taal.T("meer.openen-lukt-niet"), Taal.T("meer.offertepagina-niet-geopend"));
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        var confirmed = await ConfirmAsync(Taal.T("meer.uitloggen-vraag"),
            Taal.T("meer.uitloggen-uitleg"), Taal.T("meer.uitloggen"));
        if (!confirmed) return;
        await _auth.LogoutAsync(); // meldt ook het toestel af voor push
    }

    private static bool TryParseEur(string? text, out decimal value)
    {
        var cleaned = (text ?? "").Replace("€", "").Trim().Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
