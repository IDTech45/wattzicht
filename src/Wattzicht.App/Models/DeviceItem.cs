using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.Models;

/// <summary>Presentatiemodel voor één apparaat in de lijst.</summary>
public sealed class DeviceItem
{
    public DeviceDto Device { get; }

    /// <summary>
    /// Het piekvermogen van de installatie, als dat bekend is.
    ///
    /// Het hoort bij de woning en niet bij het apparaat, maar dit is wel de plek waar iemand het
    /// zoekt: hij kijkt naar zijn omvormer en wil weten hoeveel paneel eraan hangt. Stond het
    /// nergens, dan lijkt het alsof de app niets van je installatie weet.
    /// </summary>
    public double? PiekKw { get; }

    /// <summary>Het aantal panelen, als het merk dat meldt.</summary>
    public int? Panelen { get; }

    public DeviceItem(DeviceDto device, double? piekKw = null, int? panelen = null)
    {
        Device = device;
        PiekKw = device.Type == DeviceType.Inverter ? piekKw : null;
        Panelen = device.Type == DeviceType.Inverter ? panelen : null;
    }

    public Guid Id => Device.Id;
    public string Name => string.IsNullOrWhiteSpace(Device.Name) ? BrandName : Device.Name;
    public DeviceBrand Brand => Device.Brand;
    public bool IsOnline => Device.IsOnline;

    public string Initial => BrandName.Length > 0 ? BrandName[..1].ToUpperInvariant() : "?";

    public string BrandName => Device.Brand switch
    {
        DeviceBrand.HomeWizard => "HomeWizard",
        _ => Device.Brand.ToString()
    };

    public string TypeName => Device.Type switch
    {
        DeviceType.Inverter => Taal.T("soort.omvormer"),
        DeviceType.Battery => Taal.T("soort.thuisbatterij"),
        DeviceType.Charger => Taal.T("soort.laadpaal"),
        DeviceType.P1Meter => Taal.T("merk.p1-naam"),
        _ => Device.Type.ToString()
    };

    /// <summary>
    /// Merk, soort, en wat er op het dak ligt.
    ///
    /// Het piekvermogen en het aantal panelen komen van de omvormer zelf. Dat hoort hier te staan en
    /// niet in een invulveld: wie zijn omvormer koppelt heeft ons al verteld wat er op zijn dak ligt.
    /// </summary>
    public string Subtitle
    {
        get
        {
            var delen = new List<string> { BrandName, TypeName };

            if (Panelen is { } aantal and > 0)
            {
                delen.Add(string.Format(Taal.T(aantal == 1 ? "apparaten.paneel" : "apparaten.panelen"), aantal));
            }

            if (PiekKw is { } kwp and > 0)
            {
                delen.Add($"{kwp.ToString("0.#").Replace('.', ',')} kWp");
            }

            return string.Join(" · ", delen);
        }
    }

    /// <summary>
    /// Merken die alleen op het thuisnetwerk praten. Die leest de app zelf uit, en dus alleen als je
    /// thuis bent en de app openmaakt.
    /// </summary>
    public bool WordtThuisGelezen => Device.Brand
        is DeviceBrand.Sessy or DeviceBrand.Alfen or DeviceBrand.Enphase
        or DeviceBrand.Peblar or DeviceBrand.HomeWizard;

    /// <summary>
    /// Wat er onder het apparaat staat.
    ///
    /// Voor een apparaat in de meterkast is "offline" bijna altijd onwaar: het ding doet het prima,
    /// alleen staat de telefoon ergens anders. Dat stond er wel, met een rode stip erbij, en dan ga
    /// je zoeken naar een storing die er niet is. Nu zegt het precies wat er aan de hand is: geen
    /// wifi, of wel wifi maar niet gezien, of gewoon gelezen.
    /// </summary>
    public string StatusText
    {
        get
        {
            if (Device.IsOnline) return Taal.T("soort.online");
            if (!WordtThuisGelezen) return Taal.T("storing.offline");

            return OpWifi
                ? Taal.T("apparaten.thuis-gelezen")
                : Taal.T("apparaten.niet-op-wifi");
        }
    }

    /// <summary>
    /// De status en wanneer we het apparaat voor het laatst zagen, als één regel.
    ///
    /// Waarom één regel en niet twee labels naast elkaar: dat stond in een
    /// HorizontalStackLayout, en die breekt niet af maar snijdt af. "Wordt thuis uitgelezen · Laatst
    /// gezien 3 uur geleden" is ruim vijftig tekens en de kaart heeft er een kleine dertig; op een
    /// toestel van 1080 breed viel de helft buiten het scherm, zonder dat er iets op wees. Als tekst
    /// in één label mag hij gewoon naar de volgende regel.
    /// </summary>
    public string StatusRegel => $"{StatusText} · {LastSeenText}";

    /// <summary>Alleen een rode stip als er echt iets mis kan zijn; niet omdat jij onderweg bent.</summary>
    public bool ToonAlsProbleem => !Device.IsOnline && (!WordtThuisGelezen || OpWifi);

    /// <summary>
    /// Zit deze telefoon op wifi? Lukt het niet om dat vast te stellen, dan gaan we ervan uit van
    /// wel: liever een keer te weinig uitleg dan iemand vertellen dat hij niet op wifi zit terwijl
    /// hij thuis op de bank zit.
    /// </summary>
    private static bool OpWifi
    {
        get
        {
            try
            {
                return Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.WiFi)
                       || Connectivity.Current.ConnectionProfiles.Contains(ConnectionProfile.Ethernet);
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    /// <summary>De reden die de server of de paal gaf, als die er is.</summary>
    public string? Reden => string.IsNullOrWhiteSpace(Device.LastError) ? null : Device.LastError;

    public bool HeeftReden => Reden is not null;

    public string LastSeenText
    {
        get
        {
            // Drie gevallen, en het verschil doet ertoe: nog nooit iets binnen, net iets binnen, of
            // een tijd geleden. Vandaar het vergelijken met de losse teksten in plaats van één regel.
            if (Device.LastSeen is null) return Taal.T("home.nog-geen-meting");

            var tekst = TimeText.Ago(Device.LastSeen);
            return tekst == Taal.T("tijd.zojuist")
                ? Taal.T("apparaten.zojuist-gezien")
                : string.Format(Taal.T("apparaten.laatst-gezien"), tekst);
        }
    }
}
