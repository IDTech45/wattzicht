using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.Models;

/// <summary>Presentatiemodel voor één apparaat in de lijst.</summary>
public sealed class DeviceItem
{
    public DeviceDto Device { get; }

    public DeviceItem(DeviceDto device) => Device = device;

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

    public string Subtitle => $"{BrandName} · {TypeName}";

    public string StatusText => Taal.T(Device.IsOnline ? "soort.online" : "storing.offline");

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
