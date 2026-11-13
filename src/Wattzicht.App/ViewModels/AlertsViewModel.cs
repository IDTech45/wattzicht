using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.ViewModels;

public sealed class AlertItem
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public AlertDto Alert { get; }
    public AlertItem(AlertDto alert) => Alert = alert;

    public string DeviceName => Alert.DeviceName;

    /// <summary>
    /// De regel die vertelt wat er aan de hand is.
    ///
    /// Die bouwen we hier op en nemen we niet over van de server. Het bericht in de database is
    /// Nederlands en wordt bewaard op het moment dat de storing begint; hier weten we welke taal de
    /// gebruiker nu kiest, en de velden die we nodig hebben (soort, naam, sinds) staan er allemaal in.
    ///
    /// Bij Offline hangt de reden van de fabrikant eraan. Die laten we staan zoals hij is: hij is
    /// technisch en soms letterlijk de tekst van het apparaat, en dat weggooien kost de enige
    /// aanwijzing die er is.
    /// </summary>
    public string Message => $"{Alert.Type}" switch
    {
        "NoData" => string.Format(Taal.T("storing.tekst-geen-data"), Duur(), Alert.DeviceName),
        "ZeroProduction" => string.Format(Taal.T("storing.tekst-geen-opwek"), Alert.DeviceName),
        "Offline" => Reden() is { } reden
            ? string.Format(Taal.T("storing.tekst-offline-met-reden"), Alert.DeviceName, reden)
            : string.Format(Taal.T("storing.tekst-offline"), Alert.DeviceName),
        _ => string.IsNullOrWhiteSpace(Alert.Message) ? TypeText : Alert.Message
    };

    /// <summary>Hoe lang de storing al loopt, in woorden.</summary>
    private string Duur()
    {
        var duur = DateTimeOffset.Now - Alert.Since.ToLocalTime();
        if (duur < TimeSpan.FromHours(1))
        {
            return string.Format(Taal.T("storing.duur-minuten"), (int)Math.Max(1, duur.TotalMinutes));
        }

        return duur < TimeSpan.FromHours(48)
            ? string.Format(Taal.T("storing.duur-uren"), (int)duur.TotalHours)
            : string.Format(Taal.T("storing.duur-dagen"), (int)duur.TotalDays);
    }

    /// <summary>De reden van de fabrikant uit het serverbericht, als die erin staat.</summary>
    private string? Reden()
    {
        var bericht = Alert.Message ?? string.Empty;
        var dubbelepunt = bericht.IndexOf(": ", StringComparison.Ordinal);
        if (dubbelepunt < 0 || dubbelepunt + 2 >= bericht.Length) return null;

        var reden = bericht[(dubbelepunt + 2)..].Trim();
        return string.IsNullOrWhiteSpace(reden) ? null : reden;
    }

    public string TypeText => $"{Alert.Type}" switch
    {
        "NoData" => Taal.T("storing.geen-data"),
        "ZeroProduction" => Taal.T("storing.geen-opwek"),
        "Offline" => Taal.T("storing.offline"),
        "" => Taal.T("storing.storing"),
        var t => t
    };

    public string SinceText => TimeText.Since(Alert.Since);

    public string Advice => $"{Alert.Type}" switch
    {
        "NoData" => Taal.T("storing.advies-geen-data"),
        "ZeroProduction" => Taal.T("storing.advies-geen-opwek"),
        "Offline" => Taal.T("storing.advies-offline"),
        _ => Taal.T("storing.advies-overig")
    };
}

public partial class AlertsViewModel : BaseViewModel
{
    private readonly ApiClient _api;
    private readonly AuthService _auth;

    public ObservableCollection<AlertItem> Alerts { get; } = [];

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _hasLoaded;

    public AlertsViewModel(ApiClient api, AuthService auth)
    {
        _api = api;
        _auth = auth;
        Title = Taal.T("meldingen.storingen");
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await RunSafeAsync(async () =>
        {
            var alerts = await _api.GetAlertsAsync();
            Alerts.Clear();
            foreach (var a in alerts) Alerts.Add(new AlertItem(a));
            HasLoaded = true;
        }, _auth);
        IsRefreshing = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadAsync();
    }
}
